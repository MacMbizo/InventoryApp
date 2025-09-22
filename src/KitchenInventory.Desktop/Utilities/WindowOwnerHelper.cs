using System;
using System.Linq;
using System.Windows;

namespace KitchenInventory.Desktop.Utilities
{
    public static class WindowOwnerHelper
    {
        /// <summary>
        /// Safely assigns an owner to a dialog window where possible, avoiding self-ownership and invalid states.
        /// If an owner is assigned, sets WindowStartupLocation to CenterOwner; otherwise defaults to CenterScreen.
        /// </summary>
        public static void SetSafeOwner(Window dialog, Window? preferredOwner = null)
        {
            if (dialog == null) return;

            // Default to CenterScreen, will switch to CenterOwner if we successfully set a valid Owner
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            try
            {
                // If caller provided a preferred owner, validate and use it
                if (IsValidOwner(dialog, preferredOwner))
                {
                    dialog.Owner = preferredOwner;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    return;
                }

                // Fallback to Application.Current.MainWindow if valid
                var main = Application.Current?.MainWindow;
                if (IsValidOwner(dialog, main))
                {
                    dialog.Owner = main;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    return;
                }

                // Last resort: find any active/visible window that's not the dialog itself
                var candidate = Application.Current?.Windows
                    .OfType<Window>()
                    .Where(w => w != dialog && w.IsVisible && w.IsLoaded())
                    .OrderByDescending(w => w.IsActive)
                    .ThenByDescending(w => w.Topmost)
                    .FirstOrDefault();

                if (IsValidOwner(dialog, candidate))
                {
                    dialog.Owner = candidate;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
            }
            catch (ArgumentException)
            {
                // Swallow invalid owner assignment (e.g., self-owner) and keep CenterScreen
            }
            catch
            {
                // Any other unexpected condition should not crash dialog display
            }
        }

        /// <summary>
        /// Returns a safe owner window if available (preferredOwner -> MainWindow -> any visible window), otherwise null.
        /// Useful for dialogs that are not Windows (e.g., OpenFileDialog/SaveFileDialog/MessageBox overload selection).
        /// </summary>
        public static Window? GetSafeOwner(Window? preferredOwner = null)
        {
            try
            {
                if (IsValidOwnerCandidate(preferredOwner)) return preferredOwner!;

                var main = Application.Current?.MainWindow;
                if (IsValidOwnerCandidate(main)) return main!;

                var candidate = Application.Current?.Windows
                    .OfType<Window>()
                    .Where(w => w.IsVisible && w.IsLoaded())
                    .OrderByDescending(w => w.IsActive)
                    .ThenByDescending(w => w.Topmost)
                    .FirstOrDefault();

                if (IsValidOwnerCandidate(candidate)) return candidate!;
            }
            catch
            {
                // ignore and return null
            }
            return null;
        }

        private static bool IsValidOwner(Window dialog, Window? owner)
        {
            if (owner == null) return false;
            if (ReferenceEquals(owner, dialog)) return false;
            // Owner must be created/loaded and not closing
            if (!IsLoaded(owner)) return false;
            return true;
        }

        private static bool IsValidOwnerCandidate(Window? owner)
        {
            if (owner == null) return false;
            return IsLoaded(owner);
        }

        private static bool IsLoaded(this Window w)
        {
            // In WPF, there is no direct IsLoaded property accessible here without event context; emulate with IsInitialized and a handle to Visibility
            // Use a best-effort check: initialized and either visible or has non-default size
            try
            {
                return w.IsInitialized && (w.IsVisible || (w.Width > 0 && w.Height > 0));
            }
            catch
            {
                return false;
            }
        }
    }
}