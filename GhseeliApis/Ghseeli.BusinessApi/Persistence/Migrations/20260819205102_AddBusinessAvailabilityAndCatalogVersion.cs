using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.BusinessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessAvailabilityAndCatalogVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CatalogVersion",
                table: "Companies",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "BranchAvailabilityOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OverrideDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    StartLocalTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    EndLocalTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    SlotDurationMinutes = table.Column<int>(type: "int", nullable: true),
                    Capacity = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchAvailabilityOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchAvailabilityOverrides_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BranchAvailabilitySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MinimumLeadMinutes = table.Column<int>(type: "int", nullable: false),
                    BookingHorizonDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchAvailabilitySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchAvailabilitySettings_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BranchRecurringSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartLocalTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    EndLocalTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    SlotDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchRecurringSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchRecurringSchedules_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BranchServiceAreas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CenterLatitude = table.Column<double>(type: "float", nullable: true),
                    CenterLongitude = table.Column<double>(type: "float", nullable: true),
                    RadiusKm = table.Column<double>(type: "float", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchServiceAreas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchServiceAreas_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BranchAvailabilityOverrides_BranchId_OverrideDate",
                table: "BranchAvailabilityOverrides",
                columns: new[] { "BranchId", "OverrideDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BranchAvailabilitySettings_BranchId",
                table: "BranchAvailabilitySettings",
                column: "BranchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BranchRecurringSchedules_BranchId_DayOfWeek_IsActive",
                table: "BranchRecurringSchedules",
                columns: new[] { "BranchId", "DayOfWeek", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchServiceAreas_BranchId",
                table: "BranchServiceAreas",
                column: "BranchId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BranchAvailabilityOverrides");

            migrationBuilder.DropTable(
                name: "BranchAvailabilitySettings");

            migrationBuilder.DropTable(
                name: "BranchRecurringSchedules");

            migrationBuilder.DropTable(
                name: "BranchServiceAreas");

            migrationBuilder.DropColumn(
                name: "CatalogVersion",
                table: "Companies");
        }
    }
}
