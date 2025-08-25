using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Threading.Tasks;

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
            var result = dlg.ShowDialog();
            if (result != true) return null;
            using var stream = dlg.OpenFile();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync();
        }
    }
}