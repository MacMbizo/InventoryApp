using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using KitchenInventory.Data;
using KitchenInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KitchenInventory.Desktop.Utilities;

namespace KitchenInventory.Desktop
{
    public partial class LocationManagementDialog : Window, INotifyPropertyChanged
    {
        private readonly IDbContextFactory<KitchenInventoryDbContext> _dbFactory;
        private readonly ILogger<LocationManagementDialog> _logger;
        public ObservableCollection<Location> Locations { get; } = new();

        // Guard against race where initial load overwrites in-memory mutations
        private int _mutationEpoch = 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public LocationManagementDialog(IDbContextFactory<KitchenInventoryDbContext> dbFactory, ILogger<LocationManagementDialog> logger)
        {
            _dbFactory = dbFactory;
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
                var epochAtStart = _mutationEpoch;
                using var db = await _dbFactory.CreateDbContextAsync();
                var locs = await db.Locations.AsNoTracking().OrderBy(l => l.Name).ToListAsync();
                if (epochAtStart != _mutationEpoch) return;

                Locations.Clear();
                foreach (var l in locs) Locations.Add(l);
                _logger.LogInformation("Loaded {Count} {Noun}", Locations.Count, PluralText.Noun(Locations.Count, "location", "locations"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load locations");
                ShowError($"Failed to load locations: {ex.Message}");
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
            var name = PromptText("Enter new location name");
            if (name == null) return;
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name)) { ShowError("Name is required."); return; }

            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                if (await db.Locations.AnyAsync(l => l.Name.ToLower() == name.ToLower()))
                {
                    ShowError("A location with this name already exists.");
                    return;
                }

                var now = DateTime.UtcNow;
                var entity = new Location { Name = name, CreatedAtUtc = now, UpdatedAtUtc = now };
                db.Locations.Add(entity);
                await db.SaveChangesAsync();

                _mutationEpoch++;
                Locations.Add(entity);

                await Dispatcher.Yield(DispatcherPriority.Background);
                try
                {
                    LocationsGrid.SelectedItem = entity;
                    LocationsGrid.UpdateLayout();
                    LocationsGrid.ScrollIntoView(entity);
                    LocationsGrid.UpdateLayout();
                }
                catch { /* non-fatal UI realization attempt */ }

                ShowError(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add location");
                ShowError($"Failed to add location: {ex.Message}");
            }
        }

        private async void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (LocationsGrid.SelectedItem is not Location selected)
            {
                ShowError("Select a location to rename.");
                return;
            }
            var newName = PromptText($"Rename '{selected.Name}' to:", selected.Name);
            if (newName == null) return;
            newName = newName.Trim();
            if (string.IsNullOrWhiteSpace(newName)) { ShowError("Name is required."); return; }

            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                if (await db.Locations.AnyAsync(l => l.Id != selected.Id && l.Name.ToLower() == newName.ToLower()))
                {
                    ShowError("A location with this name already exists.");
                    return;
                }

                var entity = await db.Locations.FirstOrDefaultAsync(l => l.Id == selected.Id);
                if (entity == null)
                {
                    ShowError("Location not found.");
                    return;
                }
                entity.Name = newName;
                entity.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();

                selected.Name = newName;
                selected.UpdatedAtUtc = entity.UpdatedAtUtc;
                LocationsGrid.Items.Refresh();
                ShowError(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rename location");
                ShowError($"Failed to rename location: {ex.Message}");
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (LocationsGrid.SelectedItem is not Location selected)
            {
                ShowError("Select a location to delete.");
                return;
            }

            var owner = WindowOwnerHelper.GetSafeOwner(this) ?? this;
            var confirm = MessageBox.Show(owner,
                "Are you sure you want to delete the selected location? This will clear the location on items assigned to it, but items will not be deleted.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                var entity = await db.Locations.FirstOrDefaultAsync(l => l.Id == selected.Id);
                if (entity == null)
                {
                    ShowError("Location not found.");
                    return;
                }

                var affected = await db.Items.Where(i => i.LocationId == entity.Id).ToListAsync();
                foreach (var it in affected)
                {
                    it.LocationId = null;
                    it.UpdatedAtUtc = DateTime.UtcNow;
                }

                db.Locations.Remove(entity);
                await db.SaveChangesAsync();

                Locations.Remove(selected);
                ShowError(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete location");
                ShowError($"Failed to delete location: {ex.Message}");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private string? PromptText(string prompt, string? initial = null)
        {
            var dlg = new Window
            {
                Title = prompt,
                Width = 420,
                Height = 140,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Content = new Grid
                {
                    Margin = new Thickness(12),
                    RowDefinitions =
                    {
                        new RowDefinition{Height = new GridLength(1, GridUnitType.Star)},
                        new RowDefinition{Height = GridLength.Auto}
                    },
                    Children =
                    {
                        new TextBox{ Name = "Input", Text = initial ?? string.Empty, Margin = new Thickness(0,0,0,6)}
                    }
                }
            };

            WindowOwnerHelper.SetSafeOwner(dlg, this);

            var grid = (Grid)dlg.Content;
            var input = (TextBox)grid.Children[0];
            AutomationProperties.SetAutomationId(input, "Input");
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
            ok.Click += (_, __) => dlg.DialogResult = true;
            panel.Children.Add(ok);
            panel.Children.Add(cancel);
            Grid.SetRow(panel, 1);
            grid.Children.Add(panel);

            input.Focus();
            input.SelectAll();
            var res = dlg.ShowDialog();
            if (res == true)
                return input.Text;
            return null;
        }
    }
}
