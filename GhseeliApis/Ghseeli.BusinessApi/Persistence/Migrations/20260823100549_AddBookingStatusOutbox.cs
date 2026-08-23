using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.BusinessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingStatusOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "WorkOrders",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StatusChangedAtUtc",
                table: "AppointmentReservations",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSDATETIMEOFFSET()");

            migrationBuilder.AddColumn<long>(
                name: "StatusSequence",
                table: "AppointmentReservations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                """
                UPDATE [AppointmentReservations]
                SET [Status] = CASE WHEN [Status] = 'Reserved' THEN 'Pending' ELSE [Status] END,
                    [StatusChangedAtUtc] = TODATETIMEOFFSET([CreatedAtUtc], '+00:00');
                UPDATE [WorkOrders]
                SET [Status] = 'Pending'
                WHERE [Status] = 'Reserved';
                """);

            migrationBuilder.CreateTable(
                name: "BookingStatusOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkOrderPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DeliveryState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeadLetteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseOwner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingStatusOutboxMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingStatusOutboxMessages_AppointmentReservations_AppointmentReservationId",
                        column: x => x.AppointmentReservationId,
                        principalTable: "AppointmentReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingStatusOutboxMessages_AppointmentReservationId_Sequence",
                table: "BookingStatusOutboxMessages",
                columns: new[] { "AppointmentReservationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingStatusOutboxMessages_DeliveryState_NextAttemptAtUtc_LeaseExpiresAtUtc",
                table: "BookingStatusOutboxMessages",
                columns: new[] { "DeliveryState", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingStatusOutboxMessages");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "StatusChangedAtUtc",
                table: "AppointmentReservations");

            migrationBuilder.DropColumn(
                name: "StatusSequence",
                table: "AppointmentReservations");
        }
    }
}
