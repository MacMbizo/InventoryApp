using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using KitchenInventory.Data;
using KitchenInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KitchenInventory.Desktop
{
    public partial class CategoryManagementDialog : Window, INotifyPropertyChanged
    {
        private readonly IDbContextFactory<KitchenInventoryDbContext> _dbFactory;
        private readonly ILogger<CategoryManagementDialog> _logger;
        public ObservableCollection<Category> Categories { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public CategoryManagementDialog(IDbContextFactory<KitchenInventoryDbContext> dbFactory, ILogger<CategoryManagementDialog> logger)
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
                using var db = await _dbFactory.CreateDbContextAsync();
                var cats = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
                Categories.Clear();
                foreach (var c in cats) Categories.Add(c);
                _logger.LogInformation("Loaded {Count} categories", Categories.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load categories");
                ShowError($"Failed to load categories: {ex.Message}");
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
            var name = PromptText("Enter new category name:");
            if (name == null) return;
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name)) { ShowError("Name is required."); return; }

            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                if (await db.Categories.AnyAsync(c => c.Name.ToLower() == name.ToLower()))
                {
                    ShowError("A category with this name already exists.");
                    return;
                }

                var now = DateTime.UtcNow;
                var entity = new Category { Name = name, CreatedAtUtc = now, UpdatedAtUtc = now };
                db.Categories.Add(entity);
                await db.SaveChangesAsync();
                Categories.Add(entity);
                ShowError(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add category");
                ShowError($"Failed to add category: {ex.Message}");
            }
        }

        private async void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (CategoriesGrid.SelectedItem is not Category selected)
            {
                ShowError("Select a category to rename.");
                return;
            }
            var newName = PromptText($"Rename '{selected.Name}' to:", selected.Name);
            if (newName == null) return;
            newName = newName.Trim();
            if (string.IsNullOrWhiteSpace(newName)) { ShowError("Name is required."); return; }

            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                if (await db.Categories.AnyAsync(c => c.Id != selected.Id && c.Name.ToLower() == newName.ToLower()))
                {
                    ShowError("A category with this name already exists.");
                    return;
                }

                var entity = await db.Categories.FirstOrDefaultAsync(c => c.Id == selected.Id);
                if (entity == null)
                {
                    ShowError("Category not found.");
                    return;
                }
                entity.Name = newName;
                entity.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();

                // Update in list
                selected.Name = newName;
                selected.UpdatedAtUtc = entity.UpdatedAtUtc;
                CategoriesGrid.Items.Refresh();
                ShowError(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rename category");
                ShowError($"Failed to rename category: {ex.Message}");
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (CategoriesGrid.SelectedItem is not Category selected)
            {
                ShowError("Select a category to delete.");
                return;
            }

            var confirm = MessageBox.Show(this,
                $"Delete category '{selected.Name}'? Items in this category will not be deleted. Their category will be cleared.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                var entity = await db.Categories.FirstOrDefaultAsync(c => c.Id == selected.Id);
                if (entity == null)
                {
                    ShowError("Category not found.");
                    return;
                }

                // Set CategoryId to null for items referencing this category
                var affected = await db.Items.Where(i => i.CategoryId == entity.Id).ToListAsync();
                foreach (var it in affected)
                {
                    it.CategoryId = null;
                    it.UpdatedAtUtc = DateTime.UtcNow;
                }

                db.Categories.Remove(entity);
                await db.SaveChangesAsync();

                Categories.Remove(selected);
                ShowError(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete category");
                ShowError($"Failed to delete category: {ex.Message}");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Signal that categories may have changed
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
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
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

            var grid = (Grid)dlg.Content;
            var input = (TextBox)grid.Children[0];
            var panel = new StackPanel{Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right};
            var ok = new Button{Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0,0,6,0)};
            var cancel = new Button{Content = "Cancel", Width = 80, IsCancel = true};
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