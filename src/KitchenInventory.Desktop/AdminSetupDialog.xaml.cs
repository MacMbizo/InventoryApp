using System.Windows;
using System.Windows.Controls;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.ViewModels;
using Microsoft.Extensions.Logging;

namespace KitchenInventory.Desktop
{
    public partial class AdminSetupDialog : Window
    {
        private readonly AdminSetupViewModel _vm;

        public AdminSetupDialog(IAuthenticationService auth, ISessionService session, ILogger<AdminSetupViewModel> logger)
        {
            InitializeComponent();
            _vm = new AdminSetupViewModel(auth, session, logger);
            DataContext = _vm;
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb)
            {
                _vm.Password = pb.Password ?? string.Empty;
            }
        }

        private void OnConfirmChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb)
            {
                _vm.ConfirmPassword = pb.Password ?? string.Empty;
            }
        }

        private async void OnCreateClick(object sender, RoutedEventArgs e)
        {
            var ok = await _vm.TryCreateAdminAsync();
            if (ok)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}