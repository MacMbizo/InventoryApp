using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KitchenInventory.Desktop.Services
{
    /// <summary>
    /// Renders a <see cref="ReportTable"/> to CSV, fixed-width text and PDF.
    /// The PDF writer is dependency-free so reports work offline and in CI without
    /// pulling in a third-party (and licence-restricted) PDF library.
    /// </summary>
    public static class ReportExporter
    {
        public static string ToCsv(ReportTable table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var sb = new StringBuilder();
            sb.Append(string.Join(",", table.Headers.Select(Escape))).Append('\n');
            foreach (var row in table.Rows)
            {
                var cells = new string[table.Headers.Count];
                for (int c = 0; c < cells.Length; c++) cells[c] = Cell(row, c);
                sb.Append(string.Join(",", cells.Select(Escape))).Append('\n');
            }
            return sb.ToString();
        }

        public static string ToPlainText(ReportTable table, int maxWidth = 120)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var sb = new StringBuilder();
            sb.Append(table.Title).Append('\n');
            foreach (var line in BuildFixedWidth(table, maxWidth)) sb.Append(line).Append('\n');
            sb.Append('\n').Append(RowCountText(table.RowCount));
            return sb.ToString();
        }

        public static byte[] ToPdf(ReportTable table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var body = new List<string> { RowCountText(table.RowCount), string.Empty };
            body.AddRange(BuildFixedWidth(table, 0));
            return MinimalPdf.Render(table.Title, body);
        }

        private static string RowCountText(int count) => count == 1 ? "1 row" : $"{count} rows";

        private static string Cell(IReadOnlyList<string>? row, int index)
            => row != null && index < row.Count && row[index] != null ? row[index] : string.Empty;

        private static List<string> BuildFixedWidth(ReportTable table, int maxWidth)
        {
            var lines = new List<string>();
            var columnCount = table.Headers.Count;
            if (columnCount == 0) return lines;

            const int gap = 2;
            var widths = new int[columnCount];
            for (int c = 0; c < columnCount; c++)
            {
                var width = (table.Headers[c] ?? string.Empty).Length;
                foreach (var row in table.Rows) width = Math.Max(width, Cell(row, c).Length);
                widths[c] = Math.Clamp(width, 3, 40);
            }

            var total = widths.Sum() + gap * (columnCount - 1);
            if (maxWidth > 0 && total > maxWidth)
            {
                var available = maxWidth - gap * (columnCount - 1);
                var scale = available > 0 ? (double)available / widths.Sum() : 0.2;
                if (scale < 0.2) scale = 0.2;
                for (int c = 0; c < columnCount; c++) widths[c] = Math.Max(3, (int)Math.Floor(widths[c] * scale));
            }

            lines.Add(JoinRow(table.Headers.Select(h => h ?? string.Empty).ToList(), widths, gap));
            lines.Add(new string('-', widths.Sum() + gap * (columnCount - 1)));

            foreach (var row in table.Rows)
            {
                var cells = new List<string>(columnCount);
                for (int c = 0; c < columnCount; c++) cells.Add(Cell(row, c));
                lines.Add(JoinRow(cells, widths, gap));
            }
            return lines;
        }

        private static string JoinRow(IReadOnlyList<string> cells, int[] widths, int gap)
        {
            var parts = new string[widths.Length];
            for (int c = 0; c < widths.Length; c++)
            {
                parts[c] = PadOrTruncate(c < cells.Count ? cells[c] : string.Empty, widths[c]);
            }
            return string.Join(new string(' ', gap), parts).TrimEnd();
        }

        private static string PadOrTruncate(string text, int width)
        {
            if (text.Length == width) return text;
            if (text.Length < width) return text.PadRight(width);
            return width <= 3 ? text.Substring(0, width) : text.Substring(0, width - 3) + "...";
        }

        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            var escaped = value.Replace("\"", "\"\"");
            return needsQuotes ? $"\"{escaped}\"" : escaped;
        }

        /// <summary>
        /// Minimal, dependency-free PDF 1.4 writer: one Helvetica title plus fixed-width body
        /// lines, paginated onto A4. Output is encoded as Latin-1 so callers get a byte array
        /// that opens in any PDF reader.
        /// </summary>
        private static class MinimalPdf
        {
            private const int PageWidth = 595;
            private const int PageHeight = 842;
            private const int Margin = 40;
            private const int TitleSize = 14;
            private const int BodySize = 9;
            private const int BodyLeading = 11;
            private const int BodyLinesPerPage = 64;
            private const int MaxBodyChars = 110;

            public static byte[] Render(string title, IReadOnlyList<string> bodyLines)
            {
                var pages = Paginate(bodyLines);
                var contents = pages.Select(page => RenderContent(title, page)).ToList();

                var doc = new StringBuilder();
                doc.Append("%PDF-1.4\n");
                var offsets = new List<int>();

                const int pageObjStart = 5;
                var contentObjStart = pageObjStart + pages.Count;

                AddObject(doc, offsets, "<< /Type /Catalog /Pages 2 0 R >>");

                var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{pageObjStart + i} 0 R"));
                AddObject(doc, offsets, $"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");

                AddObject(doc, offsets, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
                AddObject(doc, offsets, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

                for (int i = 0; i < pages.Count; i++)
                {
                    AddObject(doc, offsets,
                        $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] " +
                        $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObjStart + i} 0 R >>");
                }

                foreach (var content in contents)
                {
                    AddObject(doc, offsets, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
                }

                var xrefOffset = doc.Length;
                var size = offsets.Count + 1;
                doc.Append("xref\n");
                doc.Append("0 ").Append(size).Append('\n');
                doc.Append("0000000000 65535 f \n");
                foreach (var offset in offsets) doc.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
                doc.Append("trailer\n");
                doc.Append("<< /Size ").Append(size).Append(" /Root 1 0 R >>\n");
                doc.Append("startxref\n");
                doc.Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append('\n');
                doc.Append("%%EOF\n");

                // Latin-1 is a single-byte encoding, so byte offsets equal character offsets.
                return Encoding.Latin1.GetBytes(doc.ToString());
            }

            private static List<List<string>> Paginate(IReadOnlyList<string> bodyLines)
            {
                var pages = new List<List<string>>();
                if (bodyLines.Count == 0)
                {
                    pages.Add(new List<string>());
                    return pages;
                }

                for (int i = 0; i < bodyLines.Count; i += BodyLinesPerPage)
                {
                    var page = new List<string>();
                    for (int j = i; j < Math.Min(i + BodyLinesPerPage, bodyLines.Count); j++) page.Add(bodyLines[j]);
                    pages.Add(page);
                }
                return pages;
            }

            private static string RenderContent(string title, IReadOnlyList<string> pageLines)
            {
                var sb = new StringBuilder();
                sb.Append("BT\n");
                sb.Append("/F2 ").Append(TitleSize).Append(" Tf\n");
                sb.Append(Margin).Append(' ').Append(PageHeight - Margin).Append(" Td\n");
                sb.Append('(').Append(EscapeLiteral(title)).Append(") Tj\n");
                sb.Append("/F1 ").Append(BodySize).Append(" Tf\n");
                sb.Append(BodyLeading).Append(" TL\n");
                sb.Append("0 -").Append(TitleSize + 6).Append(" Td\n");
                foreach (var line in pageLines)
                {
                    sb.Append('(').Append(EscapeLiteral(Truncate(line, MaxBodyChars))).Append(") Tj\nT*\n");
                }
                sb.Append("ET");
                return sb.ToString();
            }

            private static void AddObject(StringBuilder doc, List<int> offsets, string body)
            {
                offsets.Add(doc.Length);
                doc.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
            }

            private static string Truncate(string? value, int max)
            {
                if (string.IsNullOrEmpty(value)) return string.Empty;
                if (value.Length <= max) return value;
                return max <= 3 ? value.Substring(0, max) : value.Substring(0, max - 3) + "...";
            }

            private static string EscapeLiteral(string? value)
            {
                if (string.IsNullOrEmpty(value)) return string.Empty;
                var sb = new StringBuilder(value.Length);
                foreach (var ch in value)
                {
                    switch (ch)
                    {
                        case '\\': sb.Append("\\\\"); break;
                        case '(': sb.Append("\\("); break;
                        case ')': sb.Append("\\)"); break;
                        case '\r':
                        case '\n':
                        case '\t': sb.Append(' '); break;
                        // Latin-1/WinAnsi covers 0x00-0xFF; anything else is not representable.
                        default: sb.Append(ch <= 0xFF ? ch : '?'); break;
                    }
                }
                return sb.ToString();
            }
        }
    }
}
