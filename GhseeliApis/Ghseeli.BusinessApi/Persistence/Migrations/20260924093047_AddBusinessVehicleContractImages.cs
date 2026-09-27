using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.BusinessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessVehicleContractImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                schema: "dbo",
                table: "VehicleWorkOrderDetails",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [dbo].[VehicleWorkOrderDetails]
                SET [VehicleType] =
                    CASE
                        WHEN [VehicleType] = N'SUV' THEN N'Suv5Seater'
                        WHEN [VehicleType] IN (N'Sedan', N'Motorcycle', N'Suv5Seater', N'Suv7Seater', N'Van7Seater') THEN [VehicleType]
                        ELSE N'Sedan'
                    END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                schema: "dbo",
                table: "VehicleWorkOrderDetails");
        }
    }
}
