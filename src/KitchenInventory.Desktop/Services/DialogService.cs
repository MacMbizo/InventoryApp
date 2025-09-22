using System.Threading.Tasks;
using System.Windows;
using KitchenInventory.Desktop.Utilities;

namespace KitchenInventory.Desktop.Services
{
    public class DialogService : IDialogService
    {
        public Task<object?> ShowAsync(Window dialog)
        {
            // Safely assign owner and center appropriately
            WindowOwnerHelper.SetSafeOwner(dialog);
            var result = dialog.ShowDialog();
            if (result == true)
            {
                return Task.FromResult<object?>(dialog.Tag);
            }
            return Task.FromResult<object?>(null);
        }
    }
}