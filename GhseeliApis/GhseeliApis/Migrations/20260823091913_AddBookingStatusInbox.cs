using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhseeliApis.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingStatusInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BusinessStatusSequence",
                table: "CustomerBookings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StatusChangedAtUtc",
                table: "CustomerBookings",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSDATETIMEOFFSET()");

            migrationBuilder.Sql(
                "UPDATE [CustomerBookings] SET [Status] = CASE WHEN [Status] = 'Reserved' THEN 'Pending' ELSE [Status] END, [StatusChangedAtUtc] = [CreatedAtUtc];");

            migrationBuilder.CreateTable(
                name: "CustomerInternalServiceNonces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Nonce = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerInternalServiceNonces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedBookingStatusMessages",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Applied = table.Column<bool>(type: "bit", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedBookingStatusMessages", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_ProcessedBookingStatusMessages_CustomerBookings_CustomerBookingId",
                        column: x => x.CustomerBookingId,
                        principalTable: "CustomerBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInternalServiceNonces_ExpiresAtUtc",
                table: "CustomerInternalServiceNonces",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInternalServiceNonces_ServiceId_Nonce",
                table: "CustomerInternalServiceNonces",
                columns: new[] { "ServiceId", "Nonce" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedBookingStatusMessages_CustomerBookingId_Sequence",
                table: "ProcessedBookingStatusMessages",
                columns: new[] { "CustomerBookingId", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerInternalServiceNonces");

            migrationBuilder.DropTable(
                name: "ProcessedBookingStatusMessages");

            migrationBuilder.DropColumn(
                name: "BusinessStatusSequence",
                table: "CustomerBookings");

            migrationBuilder.DropColumn(
                name: "StatusChangedAtUtc",
                table: "CustomerBookings");
        }
    }
}
