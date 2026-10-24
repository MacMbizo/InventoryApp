using System;
using System.IO;
using System.Threading.Tasks;
using KitchenInventory.Data;
using KitchenInventory.Desktop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace KitchenInventory.Desktop.Tests
{
    public class AuthenticationServiceTests
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

        [Fact]
        public async Task CreateUser_Then_SignIn_Succeeds()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"authtest-{Guid.NewGuid()}.db");
            using var provider = BuildServices(dbPath);
            var factory = provider.GetRequiredService<IDbContextFactory<KitchenInventoryDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.MigrateAsync();
            }

            var auth = provider.GetRequiredService<IAuthenticationService>();
            var created = await auth.CreateUserAsync("alice", "Password!123", "Admin");
            Assert.True(created);

            var signIn = await auth.SignInAsync("alice", "Password!123");
            Assert.True(signIn.Success);
            Assert.Null(signIn.Error);
        }

        [Fact]
        public async Task Failed_SignIn_Increments_And_Lockout_Applies()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"authtest-{Guid.NewGuid()}.db");
            using var provider = BuildServices(dbPath);
            var factory = provider.GetRequiredService<IDbContextFactory<KitchenInventoryDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.MigrateAsync();
            }

            var auth = provider.GetRequiredService<IAuthenticationService>();
            var created = await auth.CreateUserAsync("bob", "Secret#456", "User");
            Assert.True(created);

            for (int i = 0; i < 5; i++)
            {
                var res = await auth.SignInAsync("bob", "wrong");
                Assert.False(res.Success);
            }

            var locked = await auth.SignInAsync("bob", "Secret#456");
            Assert.False(locked.Success);
            Assert.Contains("locked", locked.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}