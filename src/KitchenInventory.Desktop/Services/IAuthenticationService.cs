using System.Threading;
using System.Threading.Tasks;

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
    }
}