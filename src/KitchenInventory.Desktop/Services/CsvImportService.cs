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
        public async Task<IReadOnlyList<Item>> ParseItemsAsync(string csvContent, List<Category> categories)
        {
            return await ParseItemsInternalAsync(csvContent, categories);
        }

        public static async Task<List<Item>> ParseItemsInternalAsync(string csvContent, List<Category> categories)
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
                    ExpiryDate = expiry,
                    CreatedAtUtc = created,
                    UpdatedAtUtc = updated
                };
                results.Add(item);
            }
            return results;
        }

        private static List<string> ParseCsvLine(string line)
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