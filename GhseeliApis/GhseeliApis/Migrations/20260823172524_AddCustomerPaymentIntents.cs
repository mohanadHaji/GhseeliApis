using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhseeliApis.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPaymentIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "CustomerBookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PaymentState",
                table: "CustomerBookings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unpaid");

            migrationBuilder.CreateTable(
                name: "CustomerPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MinorAmount = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", nullable: false),
                    StripeIdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PaymentIntentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChargeId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProviderStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ClientSecret = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProviderPublishableKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IntentLeaseOwnerToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IntentLeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPayments", x => x.Id);
                    table.CheckConstraint("CK_CustomerPayments_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_CustomerPayments_Currency", "[Currency] IN ('ILS','USD','EUR')");
                    table.CheckConstraint("CK_CustomerPayments_MinorAmount", "[MinorAmount] > 0");
                    table.ForeignKey(
                        name: "FK_CustomerPayments_CustomerBookings_CustomerBookingId",
                        column: x => x.CustomerBookingId,
                        principalTable: "CustomerBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerPaymentIdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPaymentIdempotencyRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerPaymentIdempotencyRecords_CustomerPayments_CustomerPaymentId",
                        column: x => x.CustomerPaymentId,
                        principalTable: "CustomerPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StripeWebhookEvents",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BodyHash = table.Column<string>(type: "char(64)", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DispositionReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentIntentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChargeId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeWebhookEvents", x => x.EventId);
                    table.CheckConstraint("CK_StripeWebhookEvents_State", "[State] IN ('Processing','Completed','Quarantined','Deferred')");
                    table.ForeignKey(
                        name: "FK_StripeWebhookEvents_CustomerPayments_CustomerPaymentId",
                        column: x => x.CustomerPaymentId,
                        principalTable: "CustomerPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerBookings_PaymentState",
                table: "CustomerBookings",
                sql: "[PaymentState] IN ('Unpaid','Pending','Completed','Failed','Refunded')");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPaymentIdempotencyRecords_CustomerPaymentId",
                table: "CustomerPaymentIdempotencyRecords",
                column: "CustomerPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPaymentIdempotencyRecords_UserId_OwnerDeviceId_IdempotencyKey",
                table: "CustomerPaymentIdempotencyRecords",
                columns: new[] { "UserId", "OwnerDeviceId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_CustomerBookingId",
                table: "CustomerPayments",
                column: "CustomerBookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_IntentLeaseExpiresAtUtc",
                table: "CustomerPayments",
                column: "IntentLeaseExpiresAtUtc",
                filter: "[IntentLeaseOwnerToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_PaymentIntentId",
                table: "CustomerPayments",
                column: "PaymentIntentId",
                unique: true,
                filter: "[PaymentIntentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_StripeIdempotencyKey",
                table: "CustomerPayments",
                column: "StripeIdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_UserId_OwnerDeviceId_Id",
                table: "CustomerPayments",
                columns: new[] { "UserId", "OwnerDeviceId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_UserId_OwnerDeviceId_IdempotencyKey",
                table: "CustomerPayments",
                columns: new[] { "UserId", "OwnerDeviceId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookEvents_CustomerPaymentId_State_ChargeId",
                table: "StripeWebhookEvents",
                columns: new[] { "CustomerPaymentId", "State", "ChargeId" });

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookEvents_State_CreatedAtUtc",
                table: "StripeWebhookEvents",
                columns: new[] { "State", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerPaymentIdempotencyRecords");

            migrationBuilder.DropTable(
                name: "StripeWebhookEvents");

            migrationBuilder.DropTable(
                name: "CustomerPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerBookings_PaymentState",
                table: "CustomerBookings");

            migrationBuilder.DropColumn(
                name: "IsPaid",
                table: "CustomerBookings");

            migrationBuilder.DropColumn(
                name: "PaymentState",
                table: "CustomerBookings");
        }
    }
}
