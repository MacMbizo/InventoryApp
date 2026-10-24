using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using KitchenInventory.Desktop.Services;
using Microsoft.Extensions.Logging;
using KitchenInventory.Domain.Constants;

namespace KitchenInventory.Desktop.ViewModels
{
    public class AdminSetupViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
    {
        private readonly IAuthenticationService _auth;
        private readonly ISessionService _session;
        private readonly ILogger<AdminSetupViewModel> _logger;

        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _confirm = string.Empty;
        private string? _error;

        public string Username { get => _username; set { _username = value; OnPropertyChanged(); Validate(); } }
        public string Password { get => _password; set { _password = value; OnPropertyChanged(); Validate(); } }
        public string ConfirmPassword { get => _confirm; set { _confirm = value; OnPropertyChanged(); Validate(); } }
        public string? Error { get => _error; private set { _error = value; OnPropertyChanged(); } }
        public bool CanSubmit => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password) && Password == ConfirmPassword;

        public AdminSetupViewModel(IAuthenticationService auth, ISessionService session, ILogger<AdminSetupViewModel> logger)
        {
            _auth = auth;
            _session = session;
            _logger = logger;
        }

        public async Task<bool> TryCreateAdminAsync(CancellationToken ct = default)
        {
            Error = null;
            if (!CanSubmit)
            {
                Error = Password == ConfirmPassword ? "All fields are required" : "Passwords do not match";
                return false;
            }

            var created = await _auth.CreateUserAsync(Username, Password, Roles.Admin, ct);
            if (!created)
            {
                Error = "User already exists";
                return false;
            }

            _session.SetUser(Username, Roles.Admin);
            _logger.LogInformation("Initial admin '{User}' created and signed in", Username);
            return true;
        }

        private void Validate()
        {
            OnErrorsChanged(nameof(Username));
            OnErrorsChanged(nameof(Password));
            OnErrorsChanged(nameof(ConfirmPassword));
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
            if (propertyName == nameof(ConfirmPassword) && Password != ConfirmPassword)
                yield return "Passwords must match";
        }

        public bool HasErrors => string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password) || Password != ConfirmPassword;
    }
}