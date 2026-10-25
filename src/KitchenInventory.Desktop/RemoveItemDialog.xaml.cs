using System;
using System.Collections.Generic;
using System.Windows;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop
{
    /// <summary>
    /// Confirmation shown before an item is removed. It restates the item, its stock and
    /// its expiry in plain words so the user does not have to remember what they clicked,
    /// and both buttons name the action they take instead of saying "Yes"/"No".
    /// </summary>
    public partial class RemoveItemDialog : Window
    {
        public RemoveItemDialog(Item item)
        {
            ArgumentNullException.ThrowIfNull(item);
            InitializeComponent();

            var name = string.IsNullOrWhiteSpace(item.Name) ? "this item" : item.Name.Trim();
            var stock = DescribeStock(item.Quantity, item.Unit);

            HeadingText.Text = $"Remove {name} from your inventory?";
            KeepButton.Content = $"Keep {name}";

            BodyText.Text = item.Quantity == 0m
                ? $"{name} will disappear from your list. Its history is kept, so you can put it back later from History."
                : $"{name} will disappear from your list and its {stock} will be written off. Its history is kept, so you can put it back later from History.";

            IdentityText.Text = DescribeIdentity(item);

            StockRow.Visibility = item.Quantity == 0m ? Visibility.Collapsed : Visibility.Visible;
            StockValueText.Text = stock;

            if (item.ExpiryDate.HasValue)
            {
                var expired = item.ExpiryDate.Value.Date < DateTime.Today;
                ExpiryLabel.Text = expired ? "Expired" : "Expiry date";
                ExpiryValueText.Text = item.ExpiryDate.Value.ToString("d MMM yyyy");
                ExpiryRow.Visibility = Visibility.Visible;
            }
        }

        private static string DescribeStock(decimal quantity, string? unit)
            => $"{quantity:0.###} {(unit ?? string.Empty).Trim()}".Trim();

        private static string DescribeIdentity(Item item)
        {
            var parts = new List<string> { string.IsNullOrWhiteSpace(item.Name) ? "Item" : item.Name.Trim() };
            if (!string.IsNullOrWhiteSpace(item.Category?.Name)) parts.Add(item.Category!.Name!.Trim());
            if (!string.IsNullOrWhiteSpace(item.Location?.Name)) parts.Add(item.Location!.Name!.Trim());
            return string.Join("  ·  ", parts);
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            // The dialog service returns Tag when the dialog is confirmed.
            Tag = true;
            DialogResult = true;
        }
    }
}
