using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.BusinessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessVerticalReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessVerticalCode",
                schema: "dbo",
                table: "WorkOrders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "car_wash");

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessVerticalId",
                schema: "dbo",
                table: "WorkOrders",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("a842f536-17b7-4be6-a18d-1bdc6245094c"));

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessVerticalId",
                schema: "dbo",
                table: "ServiceCategories",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("a842f536-17b7-4be6-a18d-1bdc6245094c"));

            migrationBuilder.AddColumn<string>(
                name: "BusinessVerticalCode",
                schema: "dbo",
                table: "AppointmentReservations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "car_wash");

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessVerticalId",
                schema: "dbo",
                table: "AppointmentReservations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("a842f536-17b7-4be6-a18d-1bdc6245094c"));

            migrationBuilder.CreateTable(
                name: "BusinessVerticals",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RegistrationEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessVerticals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VehicleWorkOrderDetails",
                schema: "dbo",
                columns: table => new
                {
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LicensePlate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VehicleMake = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    VehicleModel = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    VehicleColor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleWorkOrderDetails", x => x.WorkOrderId);
                    table.ForeignKey(
                        name: "FK_VehicleWorkOrderDetails_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "dbo",
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyBusinessVerticals",
                schema: "dbo",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessVerticalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBusinessVerticals", x => new { x.CompanyId, x.BusinessVerticalId });
                    table.CheckConstraint("CK_CompanyBusinessVerticals_PrimaryActive", "[IsPrimary] = 0 OR [IsActive] = 1");
                    table.ForeignKey(
                        name: "FK_CompanyBusinessVerticals_BusinessVerticals_BusinessVerticalId",
                        column: x => x.BusinessVerticalId,
                        principalSchema: "dbo",
                        principalTable: "BusinessVerticals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyBusinessVerticals_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "BusinessVerticals",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "IsActive", "NameAr", "NameHe", "RegistrationEnabled" },
                values: new object[] { new Guid("a842f536-17b7-4be6-a18d-1bdc6245094c"), "car_wash", new DateTime(2026, 8, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, "غسيل السيارات", "שטיפת רכב", true });

            migrationBuilder.Sql(
                """
                INSERT INTO [dbo].[CompanyBusinessVerticals]
                    ([CompanyId], [BusinessVerticalId], [IsPrimary], [IsActive], [CreatedAtUtc])
                SELECT
                    [Id],
                    'A842F536-17B7-4BE6-A18D-1BDC6245094C',
                    1,
                    1,
                    SYSUTCDATETIME()
                FROM [dbo].[Companies];

                INSERT INTO [dbo].[VehicleWorkOrderDetails]
                    ([WorkOrderId], [VehicleType], [LicensePlate], [VehicleMake], [VehicleModel], [VehicleColor])
                SELECT
                    [Id],
                    [VehicleType],
                    [LicensePlate],
                    [VehicleMake],
                    [VehicleModel],
                    [VehicleColor]
                FROM [dbo].[WorkOrders];
                """);

            migrationBuilder.DropColumn(
                name: "LicensePlate",
                schema: "dbo",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "VehicleColor",
                schema: "dbo",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "VehicleMake",
                schema: "dbo",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "VehicleModel",
                schema: "dbo",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "VehicleType",
                schema: "dbo",
                table: "WorkOrders");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_BusinessVerticalId",
                schema: "dbo",
                table: "WorkOrders",
                column: "BusinessVerticalId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCategories_BusinessVerticalId_IsActive",
                schema: "dbo",
                table: "ServiceCategories",
                columns: new[] { "BusinessVerticalId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCategories_CompanyId_BusinessVerticalId",
                schema: "dbo",
                table: "ServiceCategories",
                columns: new[] { "CompanyId", "BusinessVerticalId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReservations_BusinessVerticalId",
                schema: "dbo",
                table: "AppointmentReservations",
                column: "BusinessVerticalId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessVerticals_Code",
                schema: "dbo",
                table: "BusinessVerticals",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBusinessVerticals_BusinessVerticalId_IsActive",
                schema: "dbo",
                table: "CompanyBusinessVerticals",
                columns: new[] { "BusinessVerticalId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBusinessVerticals_CompanyId",
                schema: "dbo",
                table: "CompanyBusinessVerticals",
                column: "CompanyId",
                unique: true,
                filter: "[IsPrimary] = 1 AND [IsActive] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentReservations_BusinessVerticals_BusinessVerticalId",
                schema: "dbo",
                table: "AppointmentReservations",
                column: "BusinessVerticalId",
                principalSchema: "dbo",
                principalTable: "BusinessVerticals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceCategories_BusinessVerticals_BusinessVerticalId",
                schema: "dbo",
                table: "ServiceCategories",
                column: "BusinessVerticalId",
                principalSchema: "dbo",
                principalTable: "BusinessVerticals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceCategories_CompanyBusinessVerticals_CompanyId_BusinessVerticalId",
                schema: "dbo",
                table: "ServiceCategories",
                columns: new[] { "CompanyId", "BusinessVerticalId" },
                principalSchema: "dbo",
                principalTable: "CompanyBusinessVerticals",
                principalColumns: new[] { "CompanyId", "BusinessVerticalId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_BusinessVerticals_BusinessVerticalId",
                schema: "dbo",
                table: "WorkOrders",
                column: "BusinessVerticalId",
                principalSchema: "dbo",
                principalTable: "BusinessVerticals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentReservations_BusinessVerticals_BusinessVerticalId",
                schema: "dbo",
                table: "AppointmentReservations");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceCategories_BusinessVerticals_BusinessVerticalId",
                schema: "dbo",
                table: "ServiceCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceCategories_CompanyBusinessVerticals_CompanyId_BusinessVerticalId",
                schema: "dbo",
                table: "ServiceCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_BusinessVerticals_BusinessVerticalId",
                schema: "dbo",
                table: "WorkOrders");

            migrationBuilder.AddColumn<string>(
                name: "LicensePlate",
                schema: "dbo",
                table: "WorkOrders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleColor",
                schema: "dbo",
                table: "WorkOrders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleMake",
                schema: "dbo",
                table: "WorkOrders",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleModel",
                schema: "dbo",
                table: "WorkOrders",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleType",
                schema: "dbo",
                table: "WorkOrders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE workOrders
                SET
                    workOrders.[VehicleType] = details.[VehicleType],
                    workOrders.[LicensePlate] = details.[LicensePlate],
                    workOrders.[VehicleMake] = details.[VehicleMake],
                    workOrders.[VehicleModel] = details.[VehicleModel],
                    workOrders.[VehicleColor] = details.[VehicleColor]
                FROM [dbo].[WorkOrders] AS workOrders
                INNER JOIN [dbo].[VehicleWorkOrderDetails] AS details
                    ON details.[WorkOrderId] = workOrders.[Id];
                """);

            migrationBuilder.DropTable(
                name: "CompanyBusinessVerticals",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "VehicleWorkOrderDetails",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BusinessVerticals",
                schema: "dbo");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_BusinessVerticalId",
                schema: "dbo",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_ServiceCategories_BusinessVerticalId_IsActive",
                schema: "dbo",
                table: "ServiceCategories");

            migrationBuilder.DropIndex(
                name: "IX_ServiceCategories_CompanyId_BusinessVerticalId",
                schema: "dbo",
                table: "ServiceCategories");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentReservations_BusinessVerticalId",
                schema: "dbo",
                table: "AppointmentReservations");

            migrationBuilder.DropColumn(
                name: "BusinessVerticalCode",
                schema: "dbo",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "BusinessVerticalId",
                schema: "dbo",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "BusinessVerticalId",
                schema: "dbo",
                table: "ServiceCategories");

            migrationBuilder.DropColumn(
                name: "BusinessVerticalCode",
                schema: "dbo",
                table: "AppointmentReservations");

            migrationBuilder.DropColumn(
                name: "BusinessVerticalId",
                schema: "dbo",
                table: "AppointmentReservations");
        }
    }
}
