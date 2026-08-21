using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhseeliApis.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogReadModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SnapshotGeneratedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSuccessfulRefreshAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastAttemptedRefreshAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastFailedRefreshAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastFailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RefreshLeaseAcquiredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefreshLeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefreshLeaseToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogBranches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AddressAr = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AddressHe = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    HasPublishedServiceArea = table.Column<bool>(type: "bit", nullable: false),
                    UsesBranchCoordinates = table.Column<bool>(type: "bit", nullable: false),
                    ServiceAreaCenterLatitude = table.Column<double>(type: "float", nullable: true),
                    ServiceAreaCenterLongitude = table.Column<double>(type: "float", nullable: true),
                    ServiceAreaRadiusKm = table.Column<double>(type: "float", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogBranches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogBranches_CatalogProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "CatalogProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogCategories_CatalogProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "CatalogProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogOfferings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceOfferingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BasePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReferenceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogOfferings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogOfferings_CatalogBranches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "CatalogBranches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CatalogOfferings_CatalogCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "CatalogCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogAddonGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAddonGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SelectionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    MinimumSelections = table.Column<int>(type: "int", nullable: false),
                    MaximumSelections = table.Column<int>(type: "int", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogAddonGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogAddonGroups_CatalogOfferings_OfferingId",
                        column: x => x.OfferingId,
                        principalTable: "CatalogOfferings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogAddonChoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAddonChoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PriceAdjustment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DurationAdjustmentMinutes = table.Column<int>(type: "int", nullable: false),
                    DefaultQuantity = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogAddonChoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogAddonChoices_CatalogAddonGroups_AddonGroupId",
                        column: x => x.AddonGroupId,
                        principalTable: "CatalogAddonGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogAddonChoices_AddonGroupId_DisplayOrder",
                table: "CatalogAddonChoices",
                columns: new[] { "AddonGroupId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogAddonChoices_SourceAddonChoiceId",
                table: "CatalogAddonChoices",
                column: "SourceAddonChoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogAddonGroups_OfferingId_DisplayOrder",
                table: "CatalogAddonGroups",
                columns: new[] { "OfferingId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogAddonGroups_SourceAddonGroupId",
                table: "CatalogAddonGroups",
                column: "SourceAddonGroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogBranches_ProviderId_DisplayOrder",
                table: "CatalogBranches",
                columns: new[] { "ProviderId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogBranches_SourceBranchId",
                table: "CatalogBranches",
                column: "SourceBranchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCategories_ProviderId_DisplayOrder",
                table: "CatalogCategories",
                columns: new[] { "ProviderId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCategories_SourceCategoryId",
                table: "CatalogCategories",
                column: "SourceCategoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogOfferings_BranchId_DisplayOrder",
                table: "CatalogOfferings",
                columns: new[] { "BranchId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogOfferings_CategoryId_DisplayOrder",
                table: "CatalogOfferings",
                columns: new[] { "CategoryId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogOfferings_SourceOfferingId",
                table: "CatalogOfferings",
                column: "SourceOfferingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProviders_IsEnabled_DisplayOrder",
                table: "CatalogProviders",
                columns: new[] { "IsEnabled", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProviders_SourceCompanyId",
                table: "CatalogProviders",
                column: "SourceCompanyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogAddonChoices");

            migrationBuilder.DropTable(
                name: "CatalogAddonGroups");

            migrationBuilder.DropTable(
                name: "CatalogOfferings");

            migrationBuilder.DropTable(
                name: "CatalogBranches");

            migrationBuilder.DropTable(
                name: "CatalogCategories");

            migrationBuilder.DropTable(
                name: "CatalogProviders");
        }
    }
}
