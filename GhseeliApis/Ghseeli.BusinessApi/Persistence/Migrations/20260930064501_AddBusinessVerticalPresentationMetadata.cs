using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.BusinessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessVerticalPresentationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BadgeCode",
                schema: "dbo",
                table: "BusinessVerticals",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ColorHex",
                schema: "dbo",
                table: "BusinessVerticals",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                schema: "dbo",
                table: "BusinessVerticals",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                schema: "dbo",
                table: "BusinessVerticals",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "BusinessVerticals",
                keyColumn: "Id",
                keyValue: new Guid("a842f536-17b7-4be6-a18d-1bdc6245094c"),
                columns: new[] { "BadgeCode", "ColorHex", "DisplayOrder", "ImageUrl" },
                values: new object[] { null, "#1A73E8", 1, "https://example.test/demo/verticals/car-wash.png" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessVerticals_IsActive_DisplayOrder",
                schema: "dbo",
                table: "BusinessVerticals",
                columns: new[] { "IsActive", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusinessVerticals_IsActive_DisplayOrder",
                schema: "dbo",
                table: "BusinessVerticals");

            migrationBuilder.DropColumn(
                name: "BadgeCode",
                schema: "dbo",
                table: "BusinessVerticals");

            migrationBuilder.DropColumn(
                name: "ColorHex",
                schema: "dbo",
                table: "BusinessVerticals");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                schema: "dbo",
                table: "BusinessVerticals");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                schema: "dbo",
                table: "BusinessVerticals");
        }
    }
}
