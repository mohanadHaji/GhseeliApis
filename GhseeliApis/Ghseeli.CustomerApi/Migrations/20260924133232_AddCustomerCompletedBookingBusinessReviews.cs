using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.CustomerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCompletedBookingBusinessReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessReviews",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDemo = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessReviews", x => x.Id);
                    table.CheckConstraint("CK_BusinessReviews_Rating", "[Rating] >= 1 AND [Rating] <= 5");
                    table.ForeignKey(
                        name: "FK_BusinessReviews_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessReviews_CustomerBookings_CustomerBookingId",
                        column: x => x.CustomerBookingId,
                        principalSchema: "dbo",
                        principalTable: "CustomerBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessReviews_BusinessSourceId_IsDemo_CreatedAtUtc_Id",
                schema: "dbo",
                table: "BusinessReviews",
                columns: new[] { "BusinessSourceId", "IsDemo", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessReviews_CustomerBookingId",
                schema: "dbo",
                table: "BusinessReviews",
                column: "CustomerBookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessReviews_UserId_CreatedAtUtc",
                schema: "dbo",
                table: "BusinessReviews",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessReviews",
                schema: "dbo");
        }
    }
}
