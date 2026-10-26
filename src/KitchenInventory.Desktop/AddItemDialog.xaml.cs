using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KitchenInventory.Domain.Entities;
using KitchenInventory.Desktop.ViewModels;
using KitchenInventory.Desktop.Utilities;

namespace KitchenInventory.Desktop
{
    public partial class AddItemDialog : Window
    {
        public List<Category> Categories { get; }
        public List<Location> Locations { get; }
        public AddItemViewModel Vm { get; }

        /// <summary>
        /// Zero when the dialog is adding something new, or the id of the item being changed.
        /// The caller uses it to tell the two apart, and the item that comes back carries it.
        /// </summary>
        public int EditedItemId { get; }

        public AddItemDialog(List<Category> categories, List<Location>? locations = null, Item? editTarget = null)
        {
            InitializeComponent();
            Categories = categories;
            Locations = locations ?? new List<Location>();
            Vm = new AddItemViewModel(categories, Locations);
            DataContext = Vm;
            Vm.ErrorsChanged += Vm_ErrorsChanged;

            if (editTarget != null)
            {
                // The same dialog and the same validation, opened on an existing item. Changing
                // an item in place through the grid looked applied but was not saved until some
                // other button was pressed; here the dialog is the only way in.
                EditedItemId = editTarget.Id;
                Title = "Edit item";
                ConfirmAddButton.Content = "_Save";
                Vm.ItemName = editTarget.Name;
                Vm.Quantity = editTarget.Quantity;
                Vm.Unit = string.IsNullOrWhiteSpace(editTarget.Unit) ? "pcs" : editTarget.Unit;
                Vm.SelectedCategoryId = editTarget.CategoryId ?? 0;
                Vm.SelectedLocationId = editTarget.LocationId ?? 0;
                Vm.ExpiryDate = editTarget.ExpiryDate;
            }
        }

        private void Vm_ErrorsChanged(object? sender, System.ComponentModel.DataErrorsChangedEventArgs e)
        {
            // No explicit IsEnabled manipulation; XAML style triggers respond to Validation.HasError
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            // Leverage ViewModel-based validation
            if (Vm.HasErrors)
            {
                FocusFirstInvalid();
                return;
            }

            var item = new Item
            {
                Id = EditedItemId,
                Name = Vm.ItemName.Trim(),
                Quantity = Vm.Quantity,
                Unit = string.IsNullOrWhiteSpace(Vm.Unit) ? "pcs" : Vm.Unit.Trim(),
                CategoryId = Vm.SelectedCategoryId > 0 ? Vm.SelectedCategoryId : null,
                LocationId = Vm.SelectedLocationId > 0 ? Vm.SelectedLocationId : null,
                ExpiryDate = Vm.ExpiryDate
            };

            Tag = item;
            DialogResult = true;
            Close();
        }

        private void Quantity_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = (TextBox)sender;
            var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
            e.Handled = !QuantityText.IsValidTyping(proposed, CultureInfo.CurrentCulture);
        }

        private void Quantity_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }

            // Take over the paste so that an amount copied from a spreadsheet or another locale
            // arrives in this machine's convention instead of being dropped without a word.
            e.CancelCommand();
            var pasted = QuantityText.NormalizePasted((string?)e.DataObject.GetData(DataFormats.Text), CultureInfo.CurrentCulture);
            var tb = (TextBox)sender;
            var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, pasted);
            if (QuantityText.IsValidTyping(proposed, CultureInfo.CurrentCulture))
            {
                tb.Text = proposed;
                tb.CaretIndex = proposed.Length;
            }
        }

        private void FocusFirstInvalid()
        {
            bool HasError(string propertyName)
            {
                var enumerable = Vm.GetErrors(propertyName) as IEnumerable;
                if (enumerable == null) return false;
                foreach (var _ in enumerable) { return true; }
                return false;
            }

            if (HasError(nameof(Vm.ItemName)))
            {
                ItemNameTextBox.Focus();
                ItemNameTextBox.SelectAll();
                return;
            }
            if (HasError(nameof(Vm.Quantity)))
            {
                QuantityTextBox.Focus();
                QuantityTextBox.SelectAll();
                return;
            }
            if (HasError(nameof(Vm.Unit)))
            {
                UnitTextBox.Focus();
                UnitTextBox.SelectAll();
                return;
            }
        }
    }
}