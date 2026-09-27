using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.CustomerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerVehicleContractsAndImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                schema: "dbo",
                table: "Vehicles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleType",
                schema: "dbo",
                table: "Vehicles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Sedan");

            migrationBuilder.AddColumn<string>(
                name: "VehicleImageUrl",
                schema: "dbo",
                table: "CustomerBookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [dbo].[CheckoutDrafts]
                SET [VehicleType] =
                    CASE
                        WHEN [VehicleType] = N'SUV' THEN N'Suv5Seater'
                        WHEN [VehicleType] IN (N'Sedan', N'Motorcycle', N'Suv5Seater', N'Suv7Seater', N'Van7Seater') THEN [VehicleType]
                        ELSE N'Sedan'
                    END;

                UPDATE [dbo].[CustomerBookings]
                SET [VehicleType] =
                    CASE
                        WHEN [VehicleType] = N'SUV' THEN N'Suv5Seater'
                        WHEN [VehicleType] IN (N'Sedan', N'Motorcycle', N'Suv5Seater', N'Suv7Seater', N'Van7Seater') THEN [VehicleType]
                        ELSE N'Sedan'
                    END;
                """);

            migrationBuilder.AddColumn<string>(
                name: "VehicleImageUrl",
                schema: "dbo",
                table: "CheckoutDrafts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                schema: "dbo",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "VehicleType",
                schema: "dbo",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "VehicleImageUrl",
                schema: "dbo",
                table: "CustomerBookings");

            migrationBuilder.DropColumn(
                name: "VehicleImageUrl",
                schema: "dbo",
                table: "CheckoutDrafts");
        }
    }
}
