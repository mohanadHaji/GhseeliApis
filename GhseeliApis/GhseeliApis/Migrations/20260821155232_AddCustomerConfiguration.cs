using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhseeliApis.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SupportEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    SupportPhone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DisplayNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayNameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LegalNoticeAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    LegalNoticeHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PrivacyPolicyUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TermsOfServiceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsMaintenanceModeEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MaintenanceMessageAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MaintenanceMessageHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerConfigurations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerConfigurations_IsActive",
                table: "CustomerConfigurations",
                column: "IsActive",
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerConfigurations");
        }
    }
}
