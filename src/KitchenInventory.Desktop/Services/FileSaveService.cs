using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace KitchenInventory.Desktop.Services
{
    public class FileSaveService : IFileSaveService
    {
        public async Task<bool> SaveTextAsAsync(string suggestedFileName, string content)
        {
            var dlg = new SaveFileDialog
            {
                FileName = suggestedFileName,
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = ".csv",
                AddExtension = true,
                OverwritePrompt = true
            };
            var result = dlg.ShowDialog();
            if (result != true) return false;

            var path = dlg.FileName;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // Write with UTF-8 BOM to help Excel recognize UTF-8
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return true;
        }
    }
}