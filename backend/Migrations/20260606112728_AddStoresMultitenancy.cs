using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddStoresMultitenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_products_ItemCode",
                table: "products");

            migrationBuilder.CreateTable(
                name: "stores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Cnpj = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "store_users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_store_users_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_store_users_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO stores ("Name", "IsActive", "CreatedAtUtc")
                VALUES ('Loja Principal', TRUE, NOW());
                """);

            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "orders",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE products
                SET "StoreId" = (SELECT "Id" FROM stores ORDER BY "Id" LIMIT 1)
                WHERE "StoreId" IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE orders
                SET "StoreId" = (SELECT "Id" FROM stores ORDER BY "Id" LIMIT 1)
                WHERE "StoreId" IS NULL;
                """);

            migrationBuilder.Sql("""
                WITH default_store AS (
                    SELECT "Id"
                    FROM stores
                    ORDER BY "Id"
                    LIMIT 1
                )
                INSERT INTO store_users ("StoreId", "UserId", "Role", "CreatedAtUtc")
                SELECT default_store."Id", users."Id", 'Owner', NOW()
                FROM users
                CROSS JOIN default_store;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "StoreId",
                table: "products",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "StoreId",
                table: "orders",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_StoreId",
                table: "products",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_products_StoreId_ItemCode",
                table: "products",
                columns: new[] { "StoreId", "ItemCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_StoreId",
                table: "orders",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_store_users_StoreId",
                table: "store_users",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_store_users_StoreId_UserId",
                table: "store_users",
                columns: new[] { "StoreId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_store_users_UserId",
                table: "store_users",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_stores_Name",
                table: "stores",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_orders_stores_StoreId",
                table: "orders",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_products_stores_StoreId",
                table: "products",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_stores_StoreId",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "FK_products_stores_StoreId",
                table: "products");

            migrationBuilder.DropTable(
                name: "store_users");

            migrationBuilder.DropTable(
                name: "stores");

            migrationBuilder.DropIndex(
                name: "IX_products_StoreId",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_StoreId_ItemCode",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_orders_StoreId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "products");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "IX_products_ItemCode",
                table: "products",
                column: "ItemCode",
                unique: true);
        }
    }
}
