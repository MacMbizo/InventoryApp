using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using KitchenInventory.Domain.Entities;
using KitchenInventory.Desktop.ViewModels;
using KitchenInventory.Desktop.Utilities;

namespace KitchenInventory.Desktop
{
    public partial class AddItemDialog : Window
    {
        public List<Category> Categories { get; }
        public AddItemViewModel Vm { get; }

        public AddItemDialog(List<Category> categories)
        {
            InitializeComponent();
            Categories = categories;
            Vm = new AddItemViewModel(categories);
            DataContext = Vm;
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
                Name = Vm.ItemName.Trim(),
                Quantity = Vm.Quantity,
                Unit = string.IsNullOrWhiteSpace(Vm.Unit) ? "pcs" : Vm.Unit.Trim(),
                CategoryId = Vm.SelectedCategoryId > 0 ? Vm.SelectedCategoryId : null,
                ExpiryDate = Vm.ExpiryDate
            };

            Tag = item;
            DialogResult = true;
            Close();
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