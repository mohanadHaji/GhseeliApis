using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhseeliApis.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerDeviceActiveState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "dbo",
                table: "CustomerDevices",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "dbo",
                table: "CustomerDevices");
        }
    }
}
