using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using KitchenInventory.Data;
using KitchenInventory.Domain.Entities;
using KitchenInventory.Desktop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KitchenInventory.Desktop.ViewModels
{
    public class LoginViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
    {
        private readonly IAuthenticationService _auth;
        private readonly IDbContextFactory<KitchenInventoryDbContext> _dbFactory;
        private readonly ISessionService _session;
        private readonly ILogger<LoginViewModel> _logger;

        private string _username = string.Empty;
        private string _password = string.Empty;
        private string? _error;

        public string Username
        {
            get => _username;
            set { _username = value; OnPropertyChanged(); Validate(); }
        }

        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(); Validate(); }
        }

        public string? Error
        {
            get => _error;
            private set { _error = value; OnPropertyChanged(); }
        }

        public bool CanSubmit => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

        public LoginViewModel(IAuthenticationService auth, IDbContextFactory<KitchenInventoryDbContext> dbFactory, ISessionService session, ILogger<LoginViewModel> logger)
        {
            _auth = auth;
            _dbFactory = dbFactory;
            _session = session;
            _logger = logger;
        }

        public async Task<bool> TryLoginAsync(CancellationToken ct = default)
        {
            Error = null;
            if (!CanSubmit)
            {
                Error = "Enter username and password";
                return false;
            }

            var (success, err) = await _auth.SignInAsync(Username, Password, ct);
            if (!success)
            {
                Error = err ?? "Invalid credentials";
                return false;
            }

            // Retrieve user role and set session
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                var user = await db.Users.SingleAsync(u => u.Username == Username && !u.IsDeleted, ct);
                _session.SetUser(user.Username, user.Role);
                _logger.LogInformation("User '{User}' signed in with role '{Role}'", user.Username, user.Role);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set session after sign-in");
                Error = "Failed to complete sign-in";
                return false;
            }

            return true;
        }

        private void Validate()
        {
            OnErrorsChanged(nameof(Username));
            OnErrorsChanged(nameof(Password));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
        private void OnErrorsChanged(string name)
            => ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(name));

        public System.Collections.IEnumerable GetErrors(string? propertyName)
        {
            if (propertyName == nameof(Username) && string.IsNullOrWhiteSpace(Username))
                yield return "Username is required";
            if (propertyName == nameof(Password) && string.IsNullOrWhiteSpace(Password))
                yield return "Password is required";
        }

        public bool HasErrors => string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password);
    }
}