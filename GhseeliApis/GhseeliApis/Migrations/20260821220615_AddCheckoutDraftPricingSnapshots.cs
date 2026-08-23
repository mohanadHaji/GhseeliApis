using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhseeliApis.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutDraftPricingSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckoutDraftPricingSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckoutDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    QuotedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                    TotalDurationMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutDraftPricingSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutDraftPricingSnapshots_CheckoutDrafts_CheckoutDraftId",
                        column: x => x.CheckoutDraftId,
                        principalTable: "CheckoutDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutDraftPricingItemSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PricingSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferingSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    BaseSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AddonSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ItemSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDurationMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutDraftPricingItemSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutDraftPricingItemSnapshots_CheckoutDraftPricingSnapshots_PricingSnapshotId",
                        column: x => x.PricingSnapshotId,
                        principalTable: "CheckoutDraftPricingSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutDraftPricingSelectionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PricingItemSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonGroupSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonChoiceSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_CheckoutDraftPricingSelectionSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutDraftPricingSelectionSnapshots_CheckoutDraftPricingItemSnapshots_PricingItemSnapshotId",
                        column: x => x.PricingItemSnapshotId,
                        principalTable: "CheckoutDraftPricingItemSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftPricingItemSnapshots_PricingSnapshotId_DisplayOrder",
                table: "CheckoutDraftPricingItemSnapshots",
                columns: new[] { "PricingSnapshotId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftPricingItemSnapshots_PricingSnapshotId_OfferingSourceId",
                table: "CheckoutDraftPricingItemSnapshots",
                columns: new[] { "PricingSnapshotId", "OfferingSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftPricingSelectionSnapshots_PricingItemSnapshotId_AddonChoiceSourceId",
                table: "CheckoutDraftPricingSelectionSnapshots",
                columns: new[] { "PricingItemSnapshotId", "AddonChoiceSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftPricingSelectionSnapshots_PricingItemSnapshotId_DisplayOrder",
                table: "CheckoutDraftPricingSelectionSnapshots",
                columns: new[] { "PricingItemSnapshotId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftPricingSnapshots_CheckoutDraftId",
                table: "CheckoutDraftPricingSnapshots",
                column: "CheckoutDraftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftPricingSnapshots_Currency_CatalogVersion_QuotedAtUtc",
                table: "CheckoutDraftPricingSnapshots",
                columns: new[] { "Currency", "CatalogVersion", "QuotedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckoutDraftPricingSelectionSnapshots");

            migrationBuilder.DropTable(
                name: "CheckoutDraftPricingItemSnapshots");

            migrationBuilder.DropTable(
                name: "CheckoutDraftPricingSnapshots");
        }
    }
}
