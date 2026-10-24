using System;
namespace KitchenInventory.Desktop.Services
{
    public interface ISessionService
    {
        string? CurrentUsername { get; }
        string? CurrentRole { get; }
        bool IsAuthenticated { get; }
        // Role/session change notification event for RBAC re-evaluation
        event EventHandler? SessionChanged;
        void SetUser(string username, string role);
        void Clear();
    }
}