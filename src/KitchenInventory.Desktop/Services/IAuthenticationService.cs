using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop.Services
{
    public interface IAuthenticationService
    {
        // Returns true if created; false if user already exists
        Task<bool> CreateUserAsync(string username, string password, string role, CancellationToken ct = default);
        // Returns (success, message). On success, message is null; otherwise contains reason
        Task<(bool Success, string? Error)> SignInAsync(string username, string password, CancellationToken ct = default);
        Task SignOutAsync();
        // Returns true if any non-deleted users exist
        Task<bool> HasAnyUsersAsync(CancellationToken ct = default);

        // Returns all non-deleted users ordered by username
        Task<IReadOnlyList<User>> GetUsersAsync(CancellationToken ct = default);
        // Updates a user's role. Prevents changing your own role and demoting the last admin.
        Task<(bool Success, string? Error)> UpdateUserRoleAsync(int userId, string newRole, string actingUsername, CancellationToken ct = default);
        // Sets a new password and clears any lockout
        Task<(bool Success, string? Error)> ResetPasswordAsync(int userId, string newPassword, CancellationToken ct = default);
        // Soft-deletes a user. Prevents deleting your own account and the last admin.
        Task<(bool Success, string? Error)> DeleteUserAsync(int userId, string actingUsername, CancellationToken ct = default);
        // Clears a lockout and resets the failed-attempt counter
        Task<(bool Success, string? Error)> UnlockUserAsync(int userId, CancellationToken ct = default);
    }
}
