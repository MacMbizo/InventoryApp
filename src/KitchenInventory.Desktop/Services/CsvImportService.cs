using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop.Services
{
    public sealed class CsvImportService : ICsvImportService
    {
        /// <summary>
        /// The columns a person can fill in, in the order the template writes them, and the
        /// header everything else is described against. Deliberately without Id: an id belongs
        /// to one installation's database, so a file exported from one machine and imported on
        /// another would update whichever item happened to hold that number.
        /// </summary>
        public const string TemplateHeader = "Name,Quantity,Unit,CategoryName,LocationName,ExpiryDate";

        /// <summary>
        /// Every column the parser understands. Only <c>Name</c> is needed, but a first line
        /// naming any of these is a header, and one naming none of them is not.
        /// </summary>
        private static readonly string[] KnownHeaders =
        {
            "Id", "Name", "Quantity", "Unit", "CategoryId", "CategoryName",
            "LocationId", "LocationName", "ExpiryDate", "CreatedAtUtc", "UpdatedAtUtc"
        };

        public async Task<IReadOnlyList<Item>> ParseItemsAsync(string csvContent, List<Category> categories, List<Location>? locations = null)
        {
            return await ParseItemsInternalAsync(csvContent, categories, locations);
        }

        /// <summary>
        /// An example of <see cref="TemplateHeader"/> with two rows: one filled in and one
        /// leaving the optional columns empty, so which ones are optional is visible rather
        /// than merely stated. The category and location are only used when they already
        /// exist, because the import refuses a name it cannot match.
        /// </summary>
        public static string BuildTemplateCsv(IEnumerable<Category>? categories = null, IEnumerable<Location>? locations = null)
        {
            var categoryName = categories?
                .FirstOrDefault(c => c.Id > 0 && !string.IsNullOrWhiteSpace(c.Name))?.Name ?? string.Empty;
            var locationName = locations?
                .FirstOrDefault(l => l.Id > 0 && !string.IsNullOrWhiteSpace(l.Name))?.Name ?? string.Empty;
            var expiry = DateTime.UtcNow.Date.AddMonths(3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            sb.Append(TemplateHeader).Append('\n');
            sb.Append("Rice,10,kg,").Append(EscapeField(categoryName)).Append(',')
              .Append(EscapeField(locationName)).Append(',').Append(expiry).Append('\n');
            sb.Append("Milk,12,L,,,\n");
            return sb.ToString();
        }

        /// <summary>
        /// Why the first line cannot be read as a header, or null when it can. Without a
        /// <c>Name</c> column every row is rejected for having no name, which reports a
        /// problem with the header as though the data were at fault.
        /// </summary>
        public static string? DescribeHeaderProblem(string? csvContent)
        {
            if (string.IsNullOrWhiteSpace(csvContent)) return "That file is empty.";

            string headerLine;
            using (var reader = new StringReader(csvContent))
            {
                headerLine = reader.ReadLine() ?? string.Empty;
            }

            var headers = ParseCsvLine(headerLine);
            if (headers.Any(h => string.Equals(h.Trim(), "Name", StringComparison.OrdinalIgnoreCase))) return null;

            var namesSomething = headers.Any(h => KnownHeaders.Contains(h.Trim(), StringComparer.OrdinalIgnoreCase));
            return namesSomething
                ? "The first line of that file names columns, but none of them is called \"Name\", "
                  + "and every row needs one. See Help > Importing from a spreadsheet for the columns "
                  + "and for a template you can fill in."
                : "The first line of that file does not name any of the columns this app reads, so "
                  + "there is nothing to bring in. The first line has to be the header. See Help > "
                  + "Importing from a spreadsheet for a template you can fill in.";
        }

        private static string EscapeField(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            var escaped = value.Replace("\"", "\"\"");
            return needsQuotes ? $"\"{escaped}\"" : escaped;
        }

        public static async Task<List<Item>> ParseItemsInternalAsync(string csvContent, List<Category> categories, List<Location>? locations = null)
        {
            var results = new List<Item>();
            if (string.IsNullOrWhiteSpace(csvContent)) return results;

            using var reader = new StringReader(csvContent);
            var headerLine = await reader.ReadLineAsync();
            if (headerLine == null) return results;

            var headers = headerLine.Split(',');
            int idxId = Array.FindIndex(headers, h => string.Equals(h.Trim(), "Id", StringComparison.OrdinalIgnoreCase));
            int idxName = Array.FindIndex(headers, h => string.Equals(h.Trim(), "Name", StringComparison.OrdinalIgnoreCase));
            int idxQty = Array.FindIndex(headers, h => string.Equals(h.Trim(), "Quantity", StringComparison.OrdinalIgnoreCase));
            int idxUnit = Array.FindIndex(headers, h => string.Equals(h.Trim(), "Unit", StringComparison.OrdinalIgnoreCase));
            int idxCategoryId = Array.FindIndex(headers, h => string.Equals(h.Trim(), "CategoryId", StringComparison.OrdinalIgnoreCase));
            int idxCategoryName = Array.FindIndex(headers, h => string.Equals(h.Trim(), "CategoryName", StringComparison.OrdinalIgnoreCase));
            int idxLocationId = Array.FindIndex(headers, h => string.Equals(h.Trim(), "LocationId", StringComparison.OrdinalIgnoreCase));
            int idxLocationName = Array.FindIndex(headers, h => string.Equals(h.Trim(), "LocationName", StringComparison.OrdinalIgnoreCase));
            int idxExpiry = Array.FindIndex(headers, h => string.Equals(h.Trim(), "ExpiryDate", StringComparison.OrdinalIgnoreCase));
            int idxCreated = Array.FindIndex(headers, h => string.Equals(h.Trim(), "CreatedAtUtc", StringComparison.OrdinalIgnoreCase));
            int idxUpdated = Array.FindIndex(headers, h => string.Equals(h.Trim(), "UpdatedAtUtc", StringComparison.OrdinalIgnoreCase));

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = ParseCsvLine(line).ToList();

                int? id = null;
                if (idxId >= 0 && idxId < cols.Count && int.TryParse(cols[idxId], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idVal))
                    id = idVal;

                var name = idxName >= 0 && idxName < cols.Count ? cols[idxName] : string.Empty;
                var unit = idxUnit >= 0 && idxUnit < cols.Count ? cols[idxUnit] : string.Empty;

                decimal qty = 0m;
                if (idxQty >= 0 && idxQty < cols.Count)
                    decimal.TryParse(cols[idxQty], NumberStyles.Number, CultureInfo.InvariantCulture, out qty);

                DateTime? expiry = null;
                if (idxExpiry >= 0 && idxExpiry < cols.Count && !string.IsNullOrWhiteSpace(cols[idxExpiry]))
                {
                    if (DateTime.TryParseExact(cols[idxExpiry], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                        expiry = dt.Date;
                }

                int? categoryId = null;
                if (idxCategoryId >= 0 && idxCategoryId < cols.Count && int.TryParse(cols[idxCategoryId], NumberStyles.Integer, CultureInfo.InvariantCulture, out var catIdVal))
                {
                    categoryId = categories.Any(c => c.Id == catIdVal) ? catIdVal : (int?)null;
                }
                else if (idxCategoryName >= 0 && idxCategoryName < cols.Count)
                {
                    var catName = cols[idxCategoryName];
                    if (!string.IsNullOrWhiteSpace(catName))
                    {
                        var match = categories.FirstOrDefault(c => string.Equals(c.Name, catName, StringComparison.OrdinalIgnoreCase));
                        if (match != null) categoryId = match.Id;
                    }
                }

                int? locationId = null;
                if (idxLocationId >= 0 && idxLocationId < cols.Count && int.TryParse(cols[idxLocationId], NumberStyles.Integer, CultureInfo.InvariantCulture, out var locIdVal))
                {
                    if (locations != null && locations.Any(l => l.Id == locIdVal)) locationId = locIdVal;
                }
                else if (idxLocationName >= 0 && idxLocationName < cols.Count && locations != null)
                {
                    var locName = cols[idxLocationName];
                    if (!string.IsNullOrWhiteSpace(locName))
                    {
                        var match = locations.FirstOrDefault(l => string.Equals(l.Name, locName, StringComparison.OrdinalIgnoreCase));
                        if (match != null) locationId = match.Id;
                    }
                }

                DateTime created = DateTime.UtcNow;
                if (idxCreated >= 0 && idxCreated < cols.Count && DateTime.TryParse(cols[idxCreated], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var createdVal))
                    created = createdVal.ToUniversalTime();

                DateTime? updated = null;
                if (idxUpdated >= 0 && idxUpdated < cols.Count && DateTime.TryParse(cols[idxUpdated], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var updatedVal))
                    updated = updatedVal.ToUniversalTime();

                var item = new Item
                {
                    Id = id.GetValueOrDefault(),
                    Name = name,
                    Quantity = qty,
                    Unit = unit,
                    CategoryId = categoryId,
                    LocationId = locationId,
                    ExpiryDate = expiry,
                    CreatedAtUtc = created,
                    UpdatedAtUtc = updated
                };
                results.Add(item);
            }
            return results;
        }

        // CSV parsing helper (exposed for analyzer)
        internal static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (line == null) return result;
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // Escaped quote
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == ',')
                    {
                        result.Add(sb.ToString());
                        sb.Clear();
                    }
                    else if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }
            result.Add(sb.ToString());
            return result;
        }
    }
}