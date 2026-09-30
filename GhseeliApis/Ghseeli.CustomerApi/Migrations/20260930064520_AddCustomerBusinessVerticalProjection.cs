using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.CustomerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerBusinessVerticalProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessVerticalBadgeCode",
                schema: "dbo",
                table: "CatalogProviders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessVerticalColorHex",
                schema: "dbo",
                table: "CatalogProviders",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BusinessVerticalDisplayOrder",
                schema: "dbo",
                table: "CatalogProviders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessVerticalId",
                schema: "dbo",
                table: "CatalogProviders",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("a842f536-17b7-4be6-a18d-1bdc6245094c"));

            migrationBuilder.AddColumn<string>(
                name: "BusinessVerticalImageUrl",
                schema: "dbo",
                table: "CatalogProviders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessVerticalNameAr",
                schema: "dbo",
                table: "CatalogProviders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "غسيل السيارات");

            migrationBuilder.AddColumn<string>(
                name: "BusinessVerticalNameHe",
                schema: "dbo",
                table: "CatalogProviders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [dbo].[CatalogProviders]
                SET [BusinessVerticalId] = 'A842F536-17B7-4BE6-A18D-1BDC6245094C',
                    [BusinessVerticalCode] = N'car_wash',
                    [BusinessVerticalNameAr] = N'غسيل السيارات',
                    [BusinessVerticalNameHe] = N'שטיפת רכב',
                    [BusinessVerticalImageUrl] = N'https://example.test/demo/verticals/car-wash.png',
                    [BusinessVerticalColorHex] = N'#1A73E8',
                    [BusinessVerticalDisplayOrder] = 1
                WHERE [BusinessVerticalCode] = N'car_wash';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProviders_BusinessVerticalId_IsEnabled_BusinessVerticalDisplayOrder",
                schema: "dbo",
                table: "CatalogProviders",
                columns: new[] { "BusinessVerticalId", "IsEnabled", "BusinessVerticalDisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CatalogProviders_BusinessVerticalId_IsEnabled_BusinessVerticalDisplayOrder",
                schema: "dbo",
                table: "CatalogProviders");

            migrationBuilder.DropColumn(
                name: "BusinessVerticalBadgeCode",
                schema: "dbo",
                table: "CatalogProviders");

            migrationBuilder.DropColumn(
                name: "BusinessVerticalColorHex",
                schema: "dbo",
                table: "CatalogProviders");

            migrationBuilder.DropColumn(
                name: "BusinessVerticalDisplayOrder",
                schema: "dbo",
                table: "CatalogProviders");

            migrationBuilder.DropColumn(
                name: "BusinessVerticalId",
                schema: "dbo",
                table: "CatalogProviders");

            migrationBuilder.DropColumn(
                name: "BusinessVerticalImageUrl",
                schema: "dbo",
                table: "CatalogProviders");

            migrationBuilder.DropColumn(
                name: "BusinessVerticalNameAr",
                schema: "dbo",
                table: "CatalogProviders");

            migrationBuilder.DropColumn(
                name: "BusinessVerticalNameHe",
                schema: "dbo",
                table: "CatalogProviders");
        }
    }
}
