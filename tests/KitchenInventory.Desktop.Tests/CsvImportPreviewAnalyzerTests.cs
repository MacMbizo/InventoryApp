using System.Collections.Generic;
using System.Threading.Tasks;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Domain.Entities;
using Xunit;

namespace KitchenInventory.Desktop.Tests
{
    public class CsvImportPreviewAnalyzerTests
    {
        [Fact]
        public async Task AnalyzeAsync_InvalidExpiryDate_ProducesIssue()
        {
            // Arrange: CSV with invalid date format for ExpiryDate
            var csv = "Id,Name,Quantity,Unit,ExpiryDate\n0,Milk,1,L,2024/12/01";
            var categories = new List<Category>();
            var existing = new List<Item>();
            var analyzer = new CsvImportPreviewAnalyzer();

            // Act
            var preview = await analyzer.AnalyzeAsync(csv, categories, existing);

            // Assert
            Assert.Equal(1, preview.Total);
            Assert.Equal(0, preview.Valid);
            Assert.Equal(1, preview.Error);
            Assert.Single(preview.Rows);
            var row = preview.Rows[0];
            Assert.False(row.IsValid);
            Assert.Contains(row.Issues, i => i.Field == "ExpiryDate" && i.Message == "Invalid date format (expected yyyy-MM-dd)." && i.Value == "2024/12/01");
        }

        [Fact]
        public async Task AnalyzeAsync_UnknownCategoryName_ProducesIssue()
        {
            // Arrange: CSV with CategoryName that does not exist
            var csv = "Id,Name,Quantity,Unit,CategoryName\n0,Bread,2,pcs,NonExistent";
            var categories = new List<Category>
            {
                new Category { Id = 1, Name = "Dairy" },
                new Category { Id = 2, Name = "Produce" }
            };
            var existing = new List<Item>();
            var analyzer = new CsvImportPreviewAnalyzer();

            // Act
            var preview = await analyzer.AnalyzeAsync(csv, categories, existing);

            // Assert
            Assert.Equal(1, preview.Total);
            Assert.Equal(0, preview.Valid);
            Assert.Equal(1, preview.Error);
            Assert.Single(preview.Rows);
            var row = preview.Rows[0];
            Assert.False(row.IsValid);
            Assert.Contains(row.Issues, i => i.Field == "CategoryName" && i.Message == "Unknown category name." && i.Value == "NonExistent");
        }
    }
}