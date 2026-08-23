using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.BusinessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingStatusDeliveryGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeliveryGeneration",
                table: "BookingStatusOutboxMessages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BookingStatusRequeueHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingStatusOutboxMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Generation = table.Column<int>(type: "int", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequeuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingStatusRequeueHistory", x => x.Id);
                    table.CheckConstraint("CK_BookingStatusRequeueHistory_Generation", "[Generation] > 0");
                    table.ForeignKey(
                        name: "FK_BookingStatusRequeueHistory_BookingStatusOutboxMessages_BookingStatusOutboxMessageId",
                        column: x => x.BookingStatusOutboxMessageId,
                        principalTable: "BookingStatusOutboxMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                UPDATE [BookingStatusOutboxMessages]
                SET [DeliveryGeneration] = 1
                WHERE [RequeuedAtUtc] IS NOT NULL;

                INSERT INTO [BookingStatusRequeueHistory]
                    ([Id], [BookingStatusOutboxMessageId], [Generation], [AdminUserId], [RequestId], [RequeuedAtUtc])
                SELECT NEWID(), [Id], 1, [RequeuedByAdminUserId], [RequeueRequestId], [RequeuedAtUtc]
                FROM [BookingStatusOutboxMessages]
                WHERE [RequeuedAtUtc] IS NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BookingStatusOutboxMessages_DeliveryGeneration",
                table: "BookingStatusOutboxMessages",
                sql: "[DeliveryGeneration] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_BookingStatusRequeueHistory_BookingStatusOutboxMessageId_Generation",
                table: "BookingStatusRequeueHistory",
                columns: new[] { "BookingStatusOutboxMessageId", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingStatusRequeueHistory_BookingStatusOutboxMessageId_RequestId",
                table: "BookingStatusRequeueHistory",
                columns: new[] { "BookingStatusOutboxMessageId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingStatusRequeueHistory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BookingStatusOutboxMessages_DeliveryGeneration",
                table: "BookingStatusOutboxMessages");

            migrationBuilder.DropColumn(
                name: "DeliveryGeneration",
                table: "BookingStatusOutboxMessages");
        }
    }
}
