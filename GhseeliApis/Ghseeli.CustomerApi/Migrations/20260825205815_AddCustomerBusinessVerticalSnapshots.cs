using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhseeliApis.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerBusinessVerticalSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessVerticalCode",
                schema: "dbo",
                table: "CustomerBookings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "car_wash");

            migrationBuilder.AddColumn<string>(
                name: "BusinessVerticalCode",
                schema: "dbo",
                table: "CatalogProviders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "car_wash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessVerticalCode",
                schema: "dbo",
                table: "CustomerBookings");

            migrationBuilder.DropColumn(
                name: "BusinessVerticalCode",
                schema: "dbo",
                table: "CatalogProviders");
        }
    }
}
