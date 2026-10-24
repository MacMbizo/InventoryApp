using System;

namespace KitchenInventory.Desktop.Services
{
    public class SessionService : ISessionService
    {
        public string? CurrentUsername { get; private set; }
        public string? CurrentRole { get; private set; }
        public bool IsAuthenticated => !string.IsNullOrEmpty(CurrentUsername);
        public event EventHandler? SessionChanged;
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
}