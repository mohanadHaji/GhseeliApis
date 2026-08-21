using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhseeliApis.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvailabilitySnapshotJson",
                table: "CatalogBranches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CheckoutDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: false),
                    PublicVersion = table.Column<int>(type: "int", nullable: false),
                    RequestedSlotStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                    RequiresReprice = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutDrafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutDraftItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckoutDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferingSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutDraftItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutDraftItems_CheckoutDrafts_CheckoutDraftId",
                        column: x => x.CheckoutDraftId,
                        principalTable: "CheckoutDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutDraftSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckoutDraftItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonGroupSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonChoiceSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutDraftSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutDraftSelections_CheckoutDraftItems_CheckoutDraftItemId",
                        column: x => x.CheckoutDraftItemId,
                        principalTable: "CheckoutDraftItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftItems_CheckoutDraftId_DisplayOrder",
                table: "CheckoutDraftItems",
                columns: new[] { "CheckoutDraftId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftItems_CheckoutDraftId_OfferingSourceId",
                table: "CheckoutDraftItems",
                columns: new[] { "CheckoutDraftId", "OfferingSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDrafts_OrderGuid",
                table: "CheckoutDrafts",
                column: "OrderGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDrafts_OwnerDeviceId_ExpiresAt",
                table: "CheckoutDrafts",
                columns: new[] { "OwnerDeviceId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDrafts_OwnerDeviceId_OrderGuid",
                table: "CheckoutDrafts",
                columns: new[] { "OwnerDeviceId", "OrderGuid" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftSelections_CheckoutDraftItemId_AddonChoiceSourceId",
                table: "CheckoutDraftSelections",
                columns: new[] { "CheckoutDraftItemId", "AddonChoiceSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftSelections_CheckoutDraftItemId_DisplayOrder",
                table: "CheckoutDraftSelections",
                columns: new[] { "CheckoutDraftItemId", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckoutDraftSelections");

            migrationBuilder.DropTable(
                name: "CheckoutDraftItems");

            migrationBuilder.DropTable(
                name: "CheckoutDrafts");

            migrationBuilder.DropColumn(
                name: "AvailabilitySnapshotJson",
                table: "CatalogBranches");
        }
    }
}
