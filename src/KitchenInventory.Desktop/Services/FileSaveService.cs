using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using KitchenInventory.Desktop.Utilities;
using System.Windows;

namespace KitchenInventory.Desktop.Services
{
    public class FileSaveService : IFileSaveService
    {
        private const string CsvFilter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

        public Task<bool> SaveTextAsAsync(string suggestedFileName, string content)
            => SaveAsync(suggestedFileName, CsvFilter, ".csv",
                // Write with UTF-8 BOM to help Excel recognize UTF-8
                path => File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)));

        public Task<bool> SaveBytesAsAsync(string suggestedFileName, byte[] content, string filter, string defaultExt)
            => SaveAsync(suggestedFileName, filter, defaultExt, path => File.WriteAllBytesAsync(path, content));

        private static async Task<bool> SaveAsync(string suggestedFileName, string filter, string defaultExt, Func<string, Task> write)
        {
            var dlg = new SaveFileDialog
            {
                FileName = suggestedFileName,
                Filter = filter,
                DefaultExt = defaultExt,
                AddExtension = true,
                OverwritePrompt = true
            };
            Window? owner = WindowOwnerHelper.GetSafeOwner();
            bool? result = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            if (result != true) return false;

            var path = dlg.FileName;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await write(path);
            return true;
        }
    }
}
