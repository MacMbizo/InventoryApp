using System.Threading.Tasks;

namespace KitchenInventory.Desktop.Services
{
    public interface IFileSaveService
    {
        Task<bool> SaveTextAsAsync(string suggestedFileName, string content);

        /// <summary>
        /// Prompts for a destination and writes raw bytes (e.g. a generated PDF report).
        /// </summary>
        Task<bool> SaveBytesAsAsync(string suggestedFileName, byte[] content, string filter, string defaultExt);
    }
}
