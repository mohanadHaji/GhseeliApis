using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhseeliApis.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerBookingSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingConfirmationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftVersion = table.Column<int>(type: "int", nullable: false),
                    ReservationRequestJson = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingConfirmationAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessWorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmedDraftVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedSlotStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RequestedSlotEndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProviderNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProviderNameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BranchNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BranchNameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VehicleType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LicensePlate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VehicleMake = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    VehicleModel = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    VehicleColor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AddressLine = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    City = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Area = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BaseSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AddonSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ItemSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ServiceFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ServiceFeeMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ServiceFeeFlatAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ServiceFeePercentageRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    TaxableSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxRatePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    TaxAppliesToServiceFee = table.Column<bool>(type: "bit", nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    QuotedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerBookings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerBookingItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferingSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ServiceNameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    BaseSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AddonSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ItemSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDurationMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBookingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerBookingItems_CustomerBookings_CustomerBookingId",
                        column: x => x.CustomerBookingId,
                        principalTable: "CustomerBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerBookingSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerBookingItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonGroupSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonChoiceSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonGroupNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AddonGroupNameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AddonChoiceNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AddonChoiceNameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SelectionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPriceAdjustment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalPriceAdjustment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitDurationAdjustmentMinutes = table.Column<int>(type: "int", nullable: false),
                    TotalDurationAdjustmentMinutes = table.Column<int>(type: "int", nullable: false),
                    IsDefaultApplied = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBookingSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerBookingSelections_CustomerBookingItems_CustomerBookingItemId",
                        column: x => x.CustomerBookingItemId,
                        principalTable: "CustomerBookingItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingConfirmationAttempts_BookingReference",
                table: "BookingConfirmationAttempts",
                column: "BookingReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingConfirmationAttempts_OrderGuid",
                table: "BookingConfirmationAttempts",
                column: "OrderGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingConfirmationAttempts_UserId_OwnerDeviceId",
                table: "BookingConfirmationAttempts",
                columns: new[] { "UserId", "OwnerDeviceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookingItems_CustomerBookingId_DisplayOrder",
                table: "CustomerBookingItems",
                columns: new[] { "CustomerBookingId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookingItems_CustomerBookingId_OfferingSourceId",
                table: "CustomerBookingItems",
                columns: new[] { "CustomerBookingId", "OfferingSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookings_BusinessReservationId",
                table: "CustomerBookings",
                column: "BusinessReservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookings_BusinessWorkOrderId",
                table: "CustomerBookings",
                column: "BusinessWorkOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookings_OrderGuid",
                table: "CustomerBookings",
                column: "OrderGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookings_OwnerDeviceId_OrderGuid",
                table: "CustomerBookings",
                columns: new[] { "OwnerDeviceId", "OrderGuid" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookings_PublicReference",
                table: "CustomerBookings",
                column: "PublicReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookings_UserId_CreatedAtUtc",
                table: "CustomerBookings",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookingSelections_CustomerBookingItemId_AddonChoiceSourceId",
                table: "CustomerBookingSelections",
                columns: new[] { "CustomerBookingItemId", "AddonChoiceSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookingSelections_CustomerBookingItemId_DisplayOrder",
                table: "CustomerBookingSelections",
                columns: new[] { "CustomerBookingItemId", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingConfirmationAttempts");

            migrationBuilder.DropTable(
                name: "CustomerBookingSelections");

            migrationBuilder.DropTable(
                name: "CustomerBookingItems");

            migrationBuilder.DropTable(
                name: "CustomerBookings");
        }
    }
}
