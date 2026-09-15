using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.CustomerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerDemoDataPartition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                schema: "dbo",
                table: "CustomerOtpChallenges",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                schema: "dbo",
                table: "CustomerDevices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                schema: "dbo",
                table: "CustomerBookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                schema: "dbo",
                table: "CheckoutDrafts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                schema: "dbo",
                table: "CatalogProviders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                schema: "dbo",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDemo",
                schema: "dbo",
                table: "CustomerOtpChallenges");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                schema: "dbo",
                table: "CustomerDevices");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                schema: "dbo",
                table: "CustomerBookings");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                schema: "dbo",
                table: "CheckoutDrafts");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                schema: "dbo",
                table: "CatalogProviders");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                schema: "dbo",
                table: "AspNetUsers");
        }
    }
}
