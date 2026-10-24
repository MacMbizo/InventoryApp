using System;
using System.IO;
using System.Threading.Tasks;
using KitchenInventory.Data;
using KitchenInventory.Domain.Entities;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace KitchenInventory.Desktop.Tests
{
    public class LoginViewModelTests
    {
        private sealed class TestSession : ISessionService
        {
            public event EventHandler? SessionChanged;
            public string? CurrentUsername { get; private set; }
            public string? CurrentRole { get; private set; }
            public bool IsAuthenticated => CurrentUsername != null;
            public void SetUser(string username, string role)
            {
                CurrentUsername = username;
                CurrentRole = role;
                SessionChanged?.Invoke(this, EventArgs.Empty);
            }
            public void Clear()
            {
                CurrentUsername = null;
                CurrentRole = null;
                SessionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private sealed class TestAuth : IAuthenticationService
        {
            public Task<bool> CreateUserAsync(string username, string password, string role, System.Threading.CancellationToken ct = default) => Task.FromResult(true);
            public Task<(bool Success, string? Error)> SignInAsync(string username, string password, System.Threading.CancellationToken ct = default)
                => Task.FromResult((true, (string?)null));
            public Task SignOutAsync() => Task.CompletedTask;
            public Task<bool> HasAnyUsersAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(true);
        }

        [Fact]
        public async Task Successful_Login_Sets_Session()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"logintest-{Guid.NewGuid()}.db");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContextFactory<KitchenInventoryDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IDbContextFactory<KitchenInventoryDbContext>>();

            // Seed a user in DB with role
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.MigrateAsync();
                db.Users.Add(new User { Username = "charlie", PasswordHash = "irrelevant", Role = "Admin", FailedLoginCount = 0, IsDeleted = false, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }

            var auth = new TestAuth();
            var session = new TestSession();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<LoginViewModel>();
            var vm = new LoginViewModel(auth, factory, session, logger)
            {
                Username = "charlie",
                Password = "anything"
            };

            var ok = await vm.TryLoginAsync();
            Assert.True(ok);
            Assert.True(session.IsAuthenticated);
            Assert.Equal("Admin", session.CurrentRole);
            Assert.Equal("charlie", session.CurrentUsername);
        }
    }
}