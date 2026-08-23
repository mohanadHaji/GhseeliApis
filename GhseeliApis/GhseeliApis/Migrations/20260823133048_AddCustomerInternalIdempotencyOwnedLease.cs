using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhseeliApis.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerInternalIdempotencyOwnedLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAtUtc",
                table: "CustomerInternalIdempotencyRecords",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerToken",
                table: "CustomerInternalIdempotencyRecords",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [CustomerInternalIdempotencyRecords]
                SET [OwnerToken] = NEWID(),
                    [LeaseExpiresAtUtc] = [ExpiresAtUtc]
                WHERE [State] = 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInternalIdempotencyRecords_OwnerToken",
                table: "CustomerInternalIdempotencyRecords",
                column: "OwnerToken",
                unique: true,
                filter: "[OwnerToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInternalIdempotencyRecords_State_LeaseExpiresAtUtc",
                table: "CustomerInternalIdempotencyRecords",
                columns: new[] { "State", "LeaseExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerInternalIdempotencyRecords_OwnerToken",
                table: "CustomerInternalIdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "IX_CustomerInternalIdempotencyRecords_State_LeaseExpiresAtUtc",
                table: "CustomerInternalIdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "CustomerInternalIdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "OwnerToken",
                table: "CustomerInternalIdempotencyRecords");
        }
    }
}
