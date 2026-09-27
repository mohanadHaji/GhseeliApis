using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.CustomerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCategoryPresentationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColorHex",
                schema: "dbo",
                table: "CatalogCategories",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                schema: "dbo",
                table: "CatalogCategories",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColorHex",
                schema: "dbo",
                table: "CatalogCategories");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                schema: "dbo",
                table: "CatalogCategories");
        }
    }
}
