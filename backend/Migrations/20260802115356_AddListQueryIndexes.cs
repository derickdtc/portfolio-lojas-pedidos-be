using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddListQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_products_StoreId_Description_Id",
                table: "products",
                columns: new[] { "StoreId", "Description", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_StoreId_Status_CreatedAtUtc",
                table: "orders",
                columns: new[] { "StoreId", "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_products_StoreId_Description_Id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_orders_StoreId_Status_CreatedAtUtc",
                table: "orders");
        }
    }
}
