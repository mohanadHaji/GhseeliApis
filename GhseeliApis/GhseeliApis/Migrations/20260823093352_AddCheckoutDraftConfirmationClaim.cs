using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhseeliApis.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260823093352_AddCheckoutDraftConfirmationClaim")]
public partial class AddCheckoutDraftConfirmationClaim : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ConfirmationBookingReference",
            table: "CheckoutDrafts",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ConfirmationClaimedAtUtc",
            table: "CheckoutDrafts",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ConfirmationClaimedVersion",
            table: "CheckoutDrafts",
            type: "int",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ConfirmationBookingReference",
            table: "CheckoutDrafts");

        migrationBuilder.DropColumn(
            name: "ConfirmationClaimedAtUtc",
            table: "CheckoutDrafts");

        migrationBuilder.DropColumn(
            name: "ConfirmationClaimedVersion",
            table: "CheckoutDrafts");
    }
}
