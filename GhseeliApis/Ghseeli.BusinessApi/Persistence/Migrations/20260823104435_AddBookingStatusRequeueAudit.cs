using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.BusinessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingStatusRequeueAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequeueRequestId",
                table: "BookingStatusOutboxMessages",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RequeuedAtUtc",
                table: "BookingStatusOutboxMessages",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequeuedByAdminUserId",
                table: "BookingStatusOutboxMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BookingStatusOutboxMessages_RequeueAudit",
                table: "BookingStatusOutboxMessages",
                sql: "([RequeuedAtUtc] IS NULL AND [RequeuedByAdminUserId] IS NULL AND [RequeueRequestId] IS NULL) OR ([RequeuedAtUtc] IS NOT NULL AND [RequeuedByAdminUserId] IS NOT NULL AND [RequeueRequestId] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BookingStatusOutboxMessages_RequeueAudit",
                table: "BookingStatusOutboxMessages");

            migrationBuilder.DropColumn(
                name: "RequeueRequestId",
                table: "BookingStatusOutboxMessages");

            migrationBuilder.DropColumn(
                name: "RequeuedAtUtc",
                table: "BookingStatusOutboxMessages");

            migrationBuilder.DropColumn(
                name: "RequeuedByAdminUserId",
                table: "BookingStatusOutboxMessages");
        }
    }
}
