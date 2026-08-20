using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.BusinessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ServiceOfferings",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ServiceCategories",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Companies",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "BranchServiceAreas",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "BranchRecurringSchedules",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Branches",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "BranchAvailabilitySettings",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "BranchAvailabilityOverrides",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "AddonGroups",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "AddonChoices",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ServiceOfferings");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ServiceCategories");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "BranchServiceAreas");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "BranchRecurringSchedules");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "BranchAvailabilitySettings");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "BranchAvailabilityOverrides");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "AddonGroups");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "AddonChoices");
        }
    }
}
