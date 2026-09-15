using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.CustomerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerDeviceOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                schema: "dbo",
                table: "CustomerDevices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDevices_UserId",
                schema: "dbo",
                table: "CustomerDevices",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerDevices_AspNetUsers_UserId",
                schema: "dbo",
                table: "CustomerDevices",
                column: "UserId",
                principalSchema: "dbo",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerDevices_AspNetUsers_UserId",
                schema: "dbo",
                table: "CustomerDevices");

            migrationBuilder.DropIndex(
                name: "IX_CustomerDevices_UserId",
                schema: "dbo",
                table: "CustomerDevices");

            migrationBuilder.DropColumn(
                name: "UserId",
                schema: "dbo",
                table: "CustomerDevices");
        }
    }
}
