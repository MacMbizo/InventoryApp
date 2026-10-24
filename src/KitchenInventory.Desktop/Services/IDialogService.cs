using System.Threading.Tasks;
using System.Windows;

namespace KitchenInventory.Desktop.Services
{
    public interface IDialogService
    {
        Task<object?> ShowAsync(Window dialog);
    }
}