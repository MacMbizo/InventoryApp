using System.Collections.Generic;
using System.Linq;
using System.Windows;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop
{
    /// <summary>One row of the column list on the importing help page.</summary>
    public sealed record ImportHelpColumn(string Column, string Needed, string Notes);

    /// <summary>
    /// What the item import expects, in the place a person would look for it. There was no
    /// help of any kind before, so the only way to learn the format was to feed the import a
    /// file and read the complaints.
    /// </summary>
    public partial class ImportHelpDialog : Window
    {
        private readonly IFileSaveService _fileSave;
        private readonly IReadOnlyList<Category> _categories;
        private readonly IReadOnlyList<Location> _locations;

        public IReadOnlyList<ImportHelpColumn> Columns { get; }

        public ImportHelpDialog(
            IFileSaveService fileSave,
            IEnumerable<Category>? categories = null,
            IEnumerable<Location>? locations = null)
        {
            InitializeComponent();
            _fileSave = fileSave;
            _categories = categories?.ToList() ?? new List<Category>();
            _locations = locations?.ToList() ?? new List<Location>();

            Columns = new List<ImportHelpColumn>
            {
                new("Name", "Always", "What the item is called. Nothing comes in without one."),
                new("Quantity", "No", "How much is on the shelf. Left blank it counts as none."),
                new("Unit", "No", "kg, L, tins and so on. Left blank it counts as pcs."),
                new("CategoryName", "No", "A category you already have, written exactly as it appears in the app."),
                new("LocationName", "No", "A location you already have, written exactly as it appears in the app."),
                new("ExpiryDate", "No", "Written as 2026-12-31. Left blank the item has no expiry date."),
            };

            DataContext = this;
        }

        private async void OnSaveTemplateClick(object sender, RoutedEventArgs e)
            => await ImportTemplateSaver.SaveAsync(_fileSave, this, _categories, _locations);
    }
}
