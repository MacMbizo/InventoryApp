using System.Threading.Tasks;

namespace KitchenInventory.Desktop.Services
{
    public interface IFileOpenService
    {
        Task<string?> OpenTextFileAsync(string filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*");
    }
}