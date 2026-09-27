using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.CustomerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerBusinessFavourites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessFavourites",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDemo = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessFavourites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessFavourites_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFavourites_BusinessSourceId_IsDemo",
                schema: "dbo",
                table: "BusinessFavourites",
                columns: new[] { "BusinessSourceId", "IsDemo" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessFavourites_UserId_BusinessSourceId_IsDemo",
                schema: "dbo",
                table: "BusinessFavourites",
                columns: new[] { "UserId", "BusinessSourceId", "IsDemo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessFavourites",
                schema: "dbo");
        }
    }
}
