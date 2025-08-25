using System.Threading.Tasks;

namespace KitchenInventory.Desktop.Services
{
    public interface IFileSaveService
    {
        Task<bool> SaveTextAsAsync(string suggestedFileName, string content);
    }
}