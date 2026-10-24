using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using KitchenInventory.Data;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KitchenInventory.Desktop
{
    public partial class LoginDialog : Window
    {
        private readonly LoginViewModel _vm;

        public LoginDialog(IAuthenticationService auth, IDbContextFactory<KitchenInventoryDbContext> dbFactory, ISessionService session, ILogger<LoginViewModel> logger)
        {
            InitializeComponent();
            _vm = new LoginViewModel(auth, dbFactory, session, logger);
            DataContext = _vm;
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb)
            {
                _vm.Password = pb.Password ?? string.Empty;
            }
        }

        private async void OnSignInClick(object sender, RoutedEventArgs e)
        {
            var ok = await _vm.TryLoginAsync();
            if (ok)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}