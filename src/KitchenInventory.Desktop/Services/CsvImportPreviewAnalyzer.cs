using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop.Services
{
    // Models used for preview
    public sealed class CsvImportIssue
    {
        public int RowNumber { get; set; }
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Value { get; set; }
    }

    public sealed class CsvImportPreviewRow
    {
        public int RowNumber { get; set; }
        public string Action { get; set; } = "Add"; // Add | Update
        public Item ParsedItem { get; set; } = new Item();
        public List<CsvImportIssue> Issues { get; set; } = new();
        public bool IsValid => Issues.Count == 0;
        public string IssueSummary => Issues.Count == 0 ? string.Empty : string.Join("; ", Issues.Select(i => i.Message));
        public string? CategoryDisplay { get; set; }
    }

    public sealed class CsvImportPreviewResult
    {
        public List<CsvImportPreviewRow> Rows { get; set; } = new();
        public List<Item> ValidItems => Rows.Where(r => r.IsValid).Select(r => r.ParsedItem).ToList();
        public int Total => Rows.Count;
        public int Valid => Rows.Count(r => r.IsValid);
        public int Error => Rows.Count(r => !r.IsValid);
        public int Adds => Rows.Count(r => r.IsValid && string.Equals(r.Action, "Add", StringComparison.OrdinalIgnoreCase));
        public int Updates => Rows.Count(r => r.IsValid && string.Equals(r.Action, "Update", StringComparison.OrdinalIgnoreCase));
    }

    public sealed class CsvImportPreviewAnalyzer
    {
        // Analyze CSV content line-by-line, producing per-row validation and action (Add/Update)
        public async Task<CsvImportPreviewResult> AnalyzeAsync(string csvContent, List<Category> categories, List<Item> existingItems)
        {
            var result = new CsvImportPreviewResult();
            if (string.IsNullOrWhiteSpace(csvContent)) return result;

            using var reader = new StringReader(csvContent);
            var headerLine = await reader.ReadLineAsync();
            if (headerLine == null) return result;

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

            var byId = existingItems.ToDictionary(i => i.Id, i => i);
            var byName = existingItems
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .GroupBy(i => i.Name!.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            string? line;
            int rowNumber = 1; // header is row 1; data starts at 2
            while ((line = await reader.ReadLineAsync()) != null)
            {
                rowNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = CsvImportService.ParseCsvLine(line);
                var issues = new List<CsvImportIssue>();

                int? id = null;
                if (idxId >= 0 && idxId < cols.Count && int.TryParse(cols[idxId], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idVal))
                    id = idVal;

                var name = idxName >= 0 && idxName < cols.Count ? cols[idxName] : string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    issues.Add(new CsvImportIssue { RowNumber = rowNumber, Field = "Name", Message = "Item name is required.", Value = name });
                }
                else if (name.Length > 200)
                {
                    issues.Add(new CsvImportIssue { RowNumber = rowNumber, Field = "Name", Message = "Item name is too long (max 200).", Value = name });
                }

                var unit = idxUnit >= 0 && idxUnit < cols.Count ? cols[idxUnit] : string.Empty;
                if (!string.IsNullOrEmpty(unit) && unit.Length > 32)
                {
                    issues.Add(new CsvImportIssue { RowNumber = rowNumber, Field = "Unit", Message = "Unit is too long (max 32).", Value = unit });
                }

                decimal qty = 0m;
                if (idxQty >= 0 && idxQty < cols.Count && !string.IsNullOrWhiteSpace(cols[idxQty]))
                    decimal.TryParse(cols[idxQty], NumberStyles.Number, CultureInfo.InvariantCulture, out qty);
                if (qty < 0m)
                    issues.Add(new CsvImportIssue { RowNumber = rowNumber, Field = "Quantity", Message = "Quantity cannot be negative.", Value = cols.ElementAtOrDefault(idxQty) });
                if (qty > 1000000m)
                    issues.Add(new CsvImportIssue { RowNumber = rowNumber, Field = "Quantity", Message = "Quantity is unrealistically large.", Value = cols.ElementAtOrDefault(idxQty) });

                DateTime? expiry = null;
                if (idxExpiry >= 0 && idxExpiry < cols.Count && !string.IsNullOrWhiteSpace(cols[idxExpiry]))
                {
                    if (DateTime.TryParseExact(cols[idxExpiry], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                        expiry = dt.Date;
                    else
                        issues.Add(new CsvImportIssue { RowNumber = rowNumber, Field = "ExpiryDate", Message = "Invalid date format (expected yyyy-MM-dd).", Value = cols[idxExpiry] });
                }

                int? categoryId = null;
                string? categoryDisplay = null;
                if (idxCategoryId >= 0 && idxCategoryId < cols.Count && int.TryParse(cols[idxCategoryId], NumberStyles.Integer, CultureInfo.InvariantCulture, out var catIdVal))
                {
                    if (categories.Any(c => c.Id == catIdVal))
                    {
                        categoryId = catIdVal;
                        categoryDisplay = categories.First(c => c.Id == catIdVal).Name;
                    }
                    else
                    {
                        issues.Add(new CsvImportIssue { RowNumber = rowNumber, Field = "CategoryId", Message = "Unknown category id.", Value = cols[idxCategoryId] });
                    }
                }
                else if (idxCategoryName >= 0 && idxCategoryName < cols.Count)
                {
                    var catName = cols[idxCategoryName];
                    if (!string.IsNullOrWhiteSpace(catName))
                    {
                        var match = categories.FirstOrDefault(c => string.Equals(c.Name, catName, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            categoryId = match.Id;
                            categoryDisplay = match.Name;
                        }
                        else
                        {
                            issues.Add(new CsvImportIssue { RowNumber = rowNumber, Field = "CategoryName", Message = "Unknown category name.", Value = catName });
                        }
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
                    Name = name ?? string.Empty,
                    Quantity = qty,
                    Unit = unit ?? string.Empty,
                    CategoryId = categoryId,
                    ExpiryDate = expiry,
                    CreatedAtUtc = created,
                    UpdatedAtUtc = updated
                };

                var row = new CsvImportPreviewRow
                {
                    RowNumber = rowNumber,
                    ParsedItem = item,
                    Issues = issues,
                    CategoryDisplay = categoryDisplay
                };

                // Determine action
                Item? existing = null;
                if (item.Id > 0 && byId.TryGetValue(item.Id, out var byIdMatch))
                {
                    existing = byIdMatch;
                }
                else if (!string.IsNullOrWhiteSpace(item.Name) && byName.TryGetValue(item.Name.Trim().ToLowerInvariant(), out var byNameMatch))
                {
                    existing = byNameMatch;
                }
                row.Action = existing != null ? "Update" : "Add";

                result.Rows.Add(row);
            }

            return result;
        }

        public static string BuildErrorsCsv(CsvImportPreviewResult preview)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Row,Field,Message,Value");
            foreach (var r in preview.Rows)
            {
                foreach (var iss in r.Issues)
                {
                    sb.Append(r.RowNumber.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(Escape(iss.Field)).Append(',')
                      .Append(Escape(iss.Message)).Append(',')
                      .Append(Escape(iss.Value)).Append('\n');
                }
            }
            return sb.ToString();
        }

        private static string Escape(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var needs = s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r');
            if (!needs) return s;
            return '"' + s.Replace("\"", "\"\"") + '"';
        }
    }
}