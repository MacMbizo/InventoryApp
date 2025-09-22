using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using KitchenInventory.Desktop.Utilities;
using System.Windows;

namespace KitchenInventory.Desktop.Services
{
    public sealed class FileOpenService : IFileOpenService
    {
        public async Task<string?> OpenTextFileAsync(string filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*")
        {
            var dlg = new OpenFileDialog
            {
                Filter = filter,
                CheckFileExists = true,
                Multiselect = false
            };

            // Prefer an owner to keep dialog modal to the app and properly centered
            Window? owner = WindowOwnerHelper.GetSafeOwner();
            bool? result = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            if (result != true) return null;

            using var stream = dlg.OpenFile();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync();
        }
    }
}