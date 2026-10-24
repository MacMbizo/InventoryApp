using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KitchenInventory.Data;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitchenInventory.Desktop.Tests
{
    public class UserManagementServiceTests
    {
        private static ServiceProvider BuildServices(string dbPath)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContextFactory<KitchenInventoryDbContext>(options =>
            {
                options.UseSqlite($"Data Source={dbPath}");
            });
            services.AddSingleton<IAuthenticationService, AuthenticationService>();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        }

        private static async Task<(ServiceProvider Provider, IAuthenticationService Auth)> CreateAsync()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"usermgmt-{Guid.NewGuid()}.db");
            var provider = BuildServices(dbPath);
            var factory = provider.GetRequiredService<IDbContextFactory<KitchenInventoryDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.MigrateAsync();
            }
            return (provider, provider.GetRequiredService<IAuthenticationService>());
        }

        [Fact]
        public async Task GetUsers_Returns_NonDeleted_Ordered_By_Username()
        {
            var (provider, auth) = await CreateAsync();
            using (provider)
            {
                await auth.CreateUserAsync("zoe", "Password!1", Roles.Staff);
                await auth.CreateUserAsync("adam", "Password!1", Roles.Admin);

                var users = await auth.GetUsersAsync();
                Assert.Equal(2, users.Count);
                Assert.Equal("adam", users[0].Username);
                Assert.Equal("zoe", users[1].Username);
            }
        }

        [Fact]
        public async Task UpdateUserRole_Changes_Role()
        {
            var (provider, auth) = await CreateAsync();
            using (provider)
            {
                await auth.CreateUserAsync("admin", "Password!1", Roles.Admin);
                await auth.CreateUserAsync("sam", "Password!1", Roles.Staff);
                var sam = (await auth.GetUsersAsync()).Single(u => u.Username == "sam");

                var result = await auth.UpdateUserRoleAsync(sam.Id, Roles.Manager, "admin");
                Assert.True(result.Success);

                var reloaded = (await auth.GetUsersAsync()).Single(u => u.Username == "sam");
                Assert.Equal(Roles.Manager, reloaded.Role);
            }
        }

        [Fact]
        public async Task UpdateUserRole_Rejects_Invalid_Role()
        {
            var (provider, auth) = await CreateAsync();
            using (provider)
            {
                await auth.CreateUserAsync("admin", "Password!1", Roles.Admin);
                var admin = (await auth.GetUsersAsync()).Single();

                var result = await auth.UpdateUserRoleAsync(admin.Id, "Superuser", "someone");
                Assert.False(result.Success);
                Assert.Contains("role", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public async Task UpdateUserRole_Blocks_Self_Demotion()
        {
            var (provider, auth) = await CreateAsync();
            using (provider)
            {
                await auth.CreateUserAsync("root", "Password!1", Roles.Admin);
                await auth.CreateUserAsync("other", "Password!1", Roles.Admin);
                var root = (await auth.GetUsersAsync()).Single(u => u.Username == "root");

                var result = await auth.UpdateUserRoleAsync(root.Id, Roles.Manager, "root");
                Assert.False(result.Success);
            }
        }

        [Fact]
        public async Task UpdateUserRole_Blocks_Demoting_Last_Admin()
        {
            var (provider, auth) = await CreateAsync();
            using (provider)
            {
                await auth.CreateUserAsync("root", "Password!1", Roles.Admin);
                await auth.CreateUserAsync("staff", "Password!1", Roles.Staff);
                var root = (await auth.GetUsersAsync()).Single(u => u.Username == "root");

                var result = await auth.UpdateUserRoleAsync(root.Id, Roles.Manager, "staff");
                Assert.False(result.Success);
                Assert.Contains("last administrator", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public async Task ResetPassword_Unlocks_Account_And_Allows_SignIn_With_New_Password()
        {
            var (provider, auth) = await CreateAsync();
            using (provider)
            {
                await auth.CreateUserAsync("bob", "OldPass!1", Roles.Staff);
                for (int i = 0; i < 5; i++)
                {
                    await auth.SignInAsync("bob", "wrong");
                }
                var locked = await auth.SignInAsync("bob", "OldPass!1");
                Assert.False(locked.Success);

                var bob = (await auth.GetUsersAsync()).Single(u => u.Username == "bob");
                var reset = await auth.ResetPasswordAsync(bob.Id, "NewPass!2");
                Assert.True(reset.Success);

                var signIn = await auth.SignInAsync("bob", "NewPass!2");
                Assert.True(signIn.Success);
            }
        }

        [Fact]
        public async Task DeleteUser_SoftDeletes_And_Blocks_SignIn()
        {
            var (provider, auth) = await CreateAsync();
            using (provider)
            {
                await auth.CreateUserAsync("admin", "Password!1", Roles.Admin);
                await auth.CreateUserAsync("gone", "Password!1", Roles.Staff);
                var gone = (await auth.GetUsersAsync()).Single(u => u.Username == "gone");

                var result = await auth.DeleteUserAsync(gone.Id, "admin");
                Assert.True(result.Success);

                Assert.DoesNotContain(await auth.GetUsersAsync(), u => u.Username == "gone");
                var signIn = await auth.SignInAsync("gone", "Password!1");
                Assert.False(signIn.Success);
            }
        }

        [Fact]
        public async Task DeleteUser_Blocks_Self_And_Last_Admin()
        {
            var (provider, auth) = await CreateAsync();
            using (provider)
            {
                await auth.CreateUserAsync("root", "Password!1", Roles.Admin);
                var root = (await auth.GetUsersAsync()).Single();

                var selfDelete = await auth.DeleteUserAsync(root.Id, "root");
                Assert.False(selfDelete.Success);
                Assert.Contains("your own account", selfDelete.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                var lastAdmin = await auth.DeleteUserAsync(root.Id, "someone");
                Assert.False(lastAdmin.Success);
                Assert.Contains("last administrator", lastAdmin.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                Assert.Single(await auth.GetUsersAsync());
            }
        }

        [Fact]
        public async Task UnlockUser_Clears_Lockout()
        {
            var (provider, auth) = await CreateAsync();
            using (provider)
            {
                await auth.CreateUserAsync("lock", "Password!1", Roles.Staff);
                for (int i = 0; i < 5; i++)
                {
                    await auth.SignInAsync("lock", "wrong");
                }
                Assert.False((await auth.SignInAsync("lock", "Password!1")).Success);

                var user = (await auth.GetUsersAsync()).Single(u => u.Username == "lock");
                var unlock = await auth.UnlockUserAsync(user.Id);
                Assert.True(unlock.Success);

                Assert.True((await auth.SignInAsync("lock", "Password!1")).Success);
            }
        }
    }
}
