using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.CustomerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerOfferingPresentationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BadgeCode",
                schema: "dbo",
                table: "CatalogOfferings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualifierAr",
                schema: "dbo",
                table: "CatalogOfferings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualifierHe",
                schema: "dbo",
                table: "CatalogOfferings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BadgeCode",
                schema: "dbo",
                table: "CatalogOfferings");

            migrationBuilder.DropColumn(
                name: "QualifierAr",
                schema: "dbo",
                table: "CatalogOfferings");

            migrationBuilder.DropColumn(
                name: "QualifierHe",
                schema: "dbo",
                table: "CatalogOfferings");
        }
    }
}
