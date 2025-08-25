using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KitchenInventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class StockMovementItemIdNullable_SetNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Items_ItemId",
                table: "StockMovements");

            migrationBuilder.AlterColumn<int>(
                name: "ItemId",
                table: "StockMovements",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Items_ItemId",
                table: "StockMovements",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Items_ItemId",
                table: "StockMovements");

            migrationBuilder.AlterColumn<int>(
                name: "ItemId",
                table: "StockMovements",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Items_ItemId",
                table: "StockMovements",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
