using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.Utilities;
using KitchenInventory.Domain.Constants;
using Microsoft.Extensions.Logging;

namespace KitchenInventory.Desktop
{
    // Lightweight row projection so the grid does not expose the password hash
    public sealed class UserRow
    {
        public int Id { get; init; }
        public string Username { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTime CreatedAtUtc { get; init; }
    }

    public partial class UserManagementDialog : Window, INotifyPropertyChanged
    {
        private readonly IAuthenticationService _auth;
        private readonly ISessionService _session;
        private readonly ILogger<UserManagementDialog> _logger;

        public ObservableCollection<UserRow> Users { get; } = new();

        private static readonly string[] AssignableRoles = { Roles.Admin, Roles.Manager, Roles.Staff };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public UserManagementDialog(IAuthenticationService auth, ISessionService session, ILogger<UserManagementDialog> logger)
        {
            _auth = auth;
            _session = session;
            _logger = logger;
            InitializeComponent();
            DataContext = this;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            try
            {
                var users = await _auth.GetUsersAsync();
                Users.Clear();
                foreach (var u in users)
                {
                    Users.Add(new UserRow
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Role = u.Role,
                        Status = (u.LockoutUntil.HasValue && u.LockoutUntil.Value > DateTime.UtcNow) ? "Locked" : "Active",
                        CreatedAtUtc = u.CreatedAtUtc
                    });
                }
                OnPropertyChanged(nameof(Users));
                _logger.LogInformation("Loaded {Count} {Noun}", Users.Count, PluralText.Noun(Users.Count, "user", "users"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load users");
                ShowError($"Failed to load users: {ex.Message}");
            }
        }

        private void ShowError(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                ErrorText.Visibility = Visibility.Collapsed;
                ErrorText.Text = string.Empty;
            }
            else
            {
                ErrorText.Visibility = Visibility.Visible;
                ErrorText.Text = message;
            }
        }

        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            var input = PromptNewUser();
            if (input == null) return;

            var (username, password, role) = input.Value;
            if (string.IsNullOrWhiteSpace(username)) { ShowError("Username is required."); return; }
            if (string.IsNullOrWhiteSpace(password)) { ShowError("Password is required."); return; }

            try
            {
                var created = await _auth.CreateUserAsync(username.Trim(), password, role);
                if (!created)
                {
                    ShowError("A user with this username already exists.");
                    return;
                }
                ShowError(null);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add user");
                ShowError($"Failed to add user: {ex.Message}");
            }
        }

        private async void ChangeRole_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not UserRow selected)
            {
                ShowError("Select a user to change role.");
                return;
            }

            var newRole = PromptRole($"Role for '{selected.Username}'", selected.Role);
            if (newRole == null) return;
            if (string.Equals(newRole, selected.Role, StringComparison.Ordinal)) return;

            try
            {
                var (ok, error) = await _auth.UpdateUserRoleAsync(selected.Id, newRole, _session.CurrentUsername ?? string.Empty);
                if (!ok)
                {
                    ShowError(error ?? "Failed to change role.");
                    return;
                }
                ShowError(null);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to change role");
                ShowError($"Failed to change role: {ex.Message}");
            }
        }

        private async void ResetPassword_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not UserRow selected)
            {
                ShowError("Select a user to reset the password.");
                return;
            }

            var password = PromptPassword($"New password for '{selected.Username}'");
            if (password == null) return;
            if (string.IsNullOrWhiteSpace(password)) { ShowError("Password is required."); return; }

            try
            {
                var (ok, error) = await _auth.ResetPasswordAsync(selected.Id, password);
                if (!ok)
                {
                    ShowError(error ?? "Failed to reset password.");
                    return;
                }
                ShowError(null);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset password");
                ShowError($"Failed to reset password: {ex.Message}");
            }
        }

        private async void Unlock_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not UserRow selected)
            {
                ShowError("Select a user to unlock.");
                return;
            }

            try
            {
                var (ok, error) = await _auth.UnlockUserAsync(selected.Id);
                if (!ok)
                {
                    ShowError(error ?? "Failed to unlock user.");
                    return;
                }
                ShowError(null);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unlock user");
                ShowError($"Failed to unlock user: {ex.Message}");
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not UserRow selected)
            {
                ShowError("Select a user to delete.");
                return;
            }

            var owner = WindowOwnerHelper.GetSafeOwner(this) ?? this;
            var confirm = MessageBox.Show(owner,
                $"Delete user '{selected.Username}'? They will no longer be able to sign in.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var (ok, error) = await _auth.DeleteUserAsync(selected.Id, _session.CurrentUsername ?? string.Empty);
                if (!ok)
                {
                    ShowError(error ?? "Failed to delete user.");
                    return;
                }
                ShowError(null);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete user");
                ShowError($"Failed to delete user: {ex.Message}");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private (string Username, string Password, string Role)? PromptNewUser()
        {
            var dlg = new Window
            {
                Title = "Add User",
                Width = 420,
                Height = 240,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };
            WindowOwnerHelper.SetSafeOwner(dlg, this);

            var grid = new Grid { Margin = new Thickness(12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 4; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var usernameBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
            AutomationProperties.SetAutomationId(usernameBox, "NewUsername");
            var passwordBox = new PasswordBox { Margin = new Thickness(0, 0, 0, 8) };
            AutomationProperties.SetAutomationId(passwordBox, "NewPassword");
            var roleCombo = new ComboBox { ItemsSource = AssignableRoles, SelectedIndex = 2, Margin = new Thickness(0, 0, 0, 8) };
            AutomationProperties.SetAutomationId(roleCombo, "NewRole");

            AddRow(grid, 0, "Username", usernameBox);
            AddRow(grid, 1, "Password", passwordBox);
            AddRow(grid, 2, "Role", roleCombo);

            var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            panel.Children.Add(ok);
            panel.Children.Add(cancel);
            ok.Click += (_, __) => dlg.DialogResult = true;
            Grid.SetRow(panel, 3);
            Grid.SetColumnSpan(panel, 2);
            grid.Children.Add(panel);

            dlg.Content = grid;
            usernameBox.Focus();

            if (dlg.ShowDialog() == true)
            {
                var role = roleCombo.SelectedItem as string ?? Roles.Staff;
                return (usernameBox.Text, passwordBox.Password, role);
            }
            return null;
        }

        private string? PromptRole(string title, string current)
        {
            var dlg = new Window
            {
                Title = title,
                Width = 380,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };
            WindowOwnerHelper.SetSafeOwner(dlg, this);

            var grid = new Grid { Margin = new Thickness(12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 2; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var roleCombo = new ComboBox { ItemsSource = AssignableRoles, Margin = new Thickness(0, 0, 0, 8) };
            var idx = Array.IndexOf(AssignableRoles, current);
            roleCombo.SelectedIndex = idx >= 0 ? idx : 2;
            AutomationProperties.SetAutomationId(roleCombo, "RoleCombo");
            AddRow(grid, 0, "Role", roleCombo);

            var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            panel.Children.Add(ok);
            panel.Children.Add(cancel);
            ok.Click += (_, __) => dlg.DialogResult = true;
            Grid.SetRow(panel, 1);
            Grid.SetColumnSpan(panel, 2);
            grid.Children.Add(panel);

            dlg.Content = grid;
            roleCombo.Focus();

            return dlg.ShowDialog() == true ? roleCombo.SelectedItem as string : null;
        }

        private string? PromptPassword(string title)
        {
            var dlg = new Window
            {
                Title = title,
                Width = 380,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };
            WindowOwnerHelper.SetSafeOwner(dlg, this);

            var grid = new Grid { Margin = new Thickness(12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 2; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var passwordBox = new PasswordBox { Margin = new Thickness(0, 0, 0, 8) };
            AutomationProperties.SetAutomationId(passwordBox, "PasswordBox");
            AddRow(grid, 0, "New Password", passwordBox);

            var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            panel.Children.Add(ok);
            panel.Children.Add(cancel);
            ok.Click += (_, __) => dlg.DialogResult = true;
            Grid.SetRow(panel, 1);
            Grid.SetColumnSpan(panel, 2);
            grid.Children.Add(panel);

            dlg.Content = grid;
            passwordBox.Focus();

            return dlg.ShowDialog() == true ? passwordBox.Password : null;
        }

        private static void AddRow(Grid grid, int row, string label, FrameworkElement control)
        {
            var text = new TextBlock { Text = label, Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }
    }
}
