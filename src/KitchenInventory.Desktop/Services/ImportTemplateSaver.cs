using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using KitchenInventory.Desktop.Utilities;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop.Services
{
    /// <summary>
    /// Writes out the example spreadsheet the import expects. It lives here rather than in one
    /// of the dialogs because both the help page and the import preview offer it, and a
    /// template handed out by either has to be the same file.
    /// </summary>
    public static class ImportTemplateSaver
    {
        public const string SuggestedFileName = "items-import-template.csv";

        public static async Task<bool> SaveAsync(
            IFileSaveService fileSave,
            Window? owner,
            IEnumerable<Category>? categories = null,
            IEnumerable<Location>? locations = null)
        {
            try
            {
                var csv = CsvImportService.BuildTemplateCsv(categories, locations);
                var saved = await fileSave.SaveTextAsAsync(SuggestedFileName, csv);
                if (saved)
                {
                    MessageBoxEx.Info(
                        "Template saved. Fill in the rows underneath the first line, saving it as CSV, "
                        + "and bring it back with \"Import items (CSV)\".",
                        owner: owner);
                }
                return saved;
            }
            catch (Exception ex)
            {
                MessageBoxEx.Error($"Could not save the template: {ex.Message}", owner: owner);
                return false;
            }
        }
    }
}
