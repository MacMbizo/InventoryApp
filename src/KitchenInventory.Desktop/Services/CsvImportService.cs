using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop.Services
{
    public sealed class CsvImportService : ICsvImportService
    {
        public Task<IReadOnlyList<Item>> ParseItemsAsync(string csvContent)
        {
            if (string.IsNullOrWhiteSpace(csvContent))
                return Task.FromResult((IReadOnlyList<Item>)new List<Item>());

            var lines = csvContent.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length == 0) return Task.FromResult((IReadOnlyList<Item>)new List<Item>());

            int start = 0;
            // Expect header: Id,Name,Quantity,Unit,ExpiryDate,CreatedAtUtc,UpdatedAtUtc
            if (lines[0].TrimStart().StartsWith("Id,", StringComparison.OrdinalIgnoreCase))
                start = 1;

            var items = new List<Item>();
            for (int i = start; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = ParseCsvLine(line);
                if (cols.Count < 4) continue;

                int id = 0;
                if (cols.Count > 0 && int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
                    id = parsedId;

                var name = cols.Count > 1 ? cols[1].Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;

                decimal qty = 0m;
                if (cols.Count > 2)
                    decimal.TryParse(cols[2], NumberStyles.Float, CultureInfo.InvariantCulture, out qty);

                var unit = cols.Count > 3 ? cols[3].Trim() : string.Empty;

                DateTime? expiry = null;
                if (cols.Count > 4 && !string.IsNullOrWhiteSpace(cols[4]))
                {
                    if (DateTime.TryParseExact(cols[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ed))
                        expiry = ed;
                }

                DateTime createdUtc = DateTime.UtcNow;
                if (cols.Count > 5 && !string.IsNullOrWhiteSpace(cols[5]))
                {
                    if (DateTime.TryParse(cols[5], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ct))
                        createdUtc = ct.ToUniversalTime();
                }

                DateTime? updatedUtc = null;
                if (cols.Count > 6 && !string.IsNullOrWhiteSpace(cols[6]))
                {
                    if (DateTime.TryParse(cols[6], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ut))
                        updatedUtc = ut.ToUniversalTime();
                }

                items.Add(new Item
                {
                    Id = id,
                    Name = name,
                    Quantity = qty,
                    Unit = unit,
                    ExpiryDate = expiry,
                    CreatedAtUtc = createdUtc,
                    UpdatedAtUtc = updatedUtc
                });
            }

            return Task.FromResult((IReadOnlyList<Item>)items);
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