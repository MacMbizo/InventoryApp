using System.Collections.Generic;
using System.Threading.Tasks;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop.Services
{
    public interface ICsvImportService
    {
        Task<IReadOnlyList<Item>> ParseItemsAsync(string csvContent);
    }
}