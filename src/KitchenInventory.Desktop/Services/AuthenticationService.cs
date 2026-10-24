using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KitchenInventory.Domain.Entities;
using KitchenInventory.Domain.Constants;
using KitchenInventory.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KitchenInventory.Desktop.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly IDbContextFactory<KitchenInventoryDbContext> _dbFactory;
        private readonly ILogger<AuthenticationService> _logger;

        private const int Iterations = 150_000; // PBKDF2 iterations
        private const int SaltSize = 16; // 128-bit salt
        private const int KeySize = 32; // 256-bit key

        public AuthenticationService(IDbContextFactory<KitchenInventoryDbContext> dbFactory, ILogger<AuthenticationService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        public async Task<bool> CreateUserAsync(string username, string password, string role, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            if (await db.Users.AnyAsync(u => u.Username == username, ct))
            {
                return false;
            }

            var hash = HashPassword(password);
            var now = DateTime.UtcNow;
            var user = new User
            {
                Username = username,
                PasswordHash = hash,
                Role = role,
                FailedLoginCount = 0,
                LockoutUntil = null,
                IsDeleted = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<(bool Success, string? Error)> SignInAsync(string username, string password, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username && !u.IsDeleted, ct);
            if (user == null)
            {
                return (false, "Invalid credentials");
            }

            if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > DateTime.UtcNow)
            {
                var minutes = Math.Max(1, (int)(user.LockoutUntil.Value - DateTime.UtcNow).TotalMinutes);
                return (false, $"Account locked. Try again in {minutes} minute(s).");
            }

            if (VerifyPassword(password, user.PasswordHash))
            {
                user.FailedLoginCount = 0;
                user.LockoutUntil = null;
                await db.SaveChangesAsync(ct);
                return (true, null);
            }
            else
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= 5)
                {
                    user.LockoutUntil = DateTime.UtcNow.AddMinutes(15);
                    user.FailedLoginCount = 0; // reset counter after lockout
                }
                await db.SaveChangesAsync(ct);
                return (false, "Invalid credentials");
            }
        }

        public Task SignOutAsync()
        {
            // For a desktop app, sign-out can clear any in-memory session state. Placeholder.
            return Task.CompletedTask;
        }

        // Returns true if any non-deleted users exist
        public async Task<bool> HasAnyUsersAsync(CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            return await db.Users.AnyAsync(u => !u.IsDeleted, ct);
        }

        private static string HashPassword(string password)
        {
            using var rng = RandomNumberGenerator.Create();
            var salt = new byte[SaltSize];
            rng.GetBytes(salt);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
            var key = pbkdf2.GetBytes(KeySize);
            return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
        }

        private static bool VerifyPassword(string password, string hash)
        {
            var parts = hash.Split('.');
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[0], out var iterations)) return false;
            var salt = Convert.FromBase64String(parts[1]);
            var key = Convert.FromBase64String(parts[2]);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
            var keyToCheck = pbkdf2.GetBytes(KeySize);
            return CryptographicOperations.FixedTimeEquals(key, keyToCheck);
        }
    }
}