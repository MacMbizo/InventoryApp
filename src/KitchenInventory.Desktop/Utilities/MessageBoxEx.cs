using System;
using System.Windows;

namespace KitchenInventory.Desktop.Utilities
{
    /// <summary>
    /// Centralized helper for showing message boxes with consistent owner, title, and icon defaults.
    /// </summary>
    public static class MessageBoxEx
    {
        internal const string DefaultTitle = "Kitchen Inventory";

        public static MessageBoxResult Show(string message, string? title = null, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, Window? owner = null)
        {
            owner = WindowOwnerHelper.GetSafeOwner(owner);
            var resolvedTitle = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title!;
            return owner != null
                ? MessageBox.Show(owner, message, resolvedTitle, buttons, icon)
                : MessageBox.Show(message, resolvedTitle, buttons, icon);
        }

        public static MessageBoxResult Info(string message, string? title = null, Window? owner = null)
            => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information, owner);

        public static MessageBoxResult Warning(string message, string? title = null, Window? owner = null)
            => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning, owner);

        public static MessageBoxResult Error(string message, string? title = null, Window? owner = null)
            => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error, owner);

        public static bool Confirm(string message, string? title = null, Window? owner = null)
            => Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question, owner) == MessageBoxResult.OK;
    }
}