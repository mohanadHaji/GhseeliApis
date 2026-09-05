using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhseeliApis.Migrations;

/// <inheritdoc />
public partial class ReplaceStripeWithLahzaCustomerPayments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_CustomerPayments_IntentLeaseExpiresAtUtc",
            schema: "dbo",
            table: "CustomerPayments");
        migrationBuilder.DropIndex(
            name: "IX_CustomerPayments_PaymentIntentId",
            schema: "dbo",
            table: "CustomerPayments");
        migrationBuilder.DropIndex(
            name: "IX_CustomerPayments_StripeIdempotencyKey",
            schema: "dbo",
            table: "CustomerPayments");
        migrationBuilder.DropCheckConstraint(
            name: "CK_CustomerPayments_Currency",
            schema: "dbo",
            table: "CustomerPayments");

        migrationBuilder.DropForeignKey(
            name: "FK_StripeWebhookEvents_CustomerPayments_CustomerPaymentId",
            schema: "dbo",
            table: "StripeWebhookEvents");
        migrationBuilder.DropPrimaryKey(
            name: "PK_StripeWebhookEvents",
            schema: "dbo",
            table: "StripeWebhookEvents");
        migrationBuilder.DropCheckConstraint(
            name: "CK_StripeWebhookEvents_State",
            schema: "dbo",
            table: "StripeWebhookEvents");
        migrationBuilder.DropIndex(
            name: "IX_StripeWebhookEvents_CustomerPaymentId_State_ChargeId",
            schema: "dbo",
            table: "StripeWebhookEvents");
        migrationBuilder.DropIndex(
            name: "IX_StripeWebhookEvents_State_CreatedAtUtc",
            schema: "dbo",
            table: "StripeWebhookEvents");

        migrationBuilder.RenameTable(
            name: "StripeWebhookEvents",
            schema: "dbo",
            newName: "PaymentWebhookEvents",
            newSchema: "dbo");
        migrationBuilder.RenameColumn(
            name: "PaymentIntentId",
            schema: "dbo",
            table: "CustomerPayments",
            newName: "ProviderReference");
        migrationBuilder.RenameColumn(
            name: "ChargeId",
            schema: "dbo",
            table: "CustomerPayments",
            newName: "ProviderTransactionId");
        migrationBuilder.RenameColumn(
            name: "IntentLeaseOwnerToken",
            schema: "dbo",
            table: "CustomerPayments",
            newName: "InitializationLeaseOwnerToken");
        migrationBuilder.RenameColumn(
            name: "IntentLeaseExpiresAtUtc",
            schema: "dbo",
            table: "CustomerPayments",
            newName: "InitializationLeaseExpiresAtUtc");
        migrationBuilder.RenameColumn(
            name: "PaymentIntentId",
            schema: "dbo",
            table: "PaymentWebhookEvents",
            newName: "ProviderReference");
        migrationBuilder.RenameColumn(
            name: "ChargeId",
            schema: "dbo",
            table: "PaymentWebhookEvents",
            newName: "ProviderTransactionId");

        migrationBuilder.DropColumn(
            name: "ClientSecret",
            schema: "dbo",
            table: "CustomerPayments");
        migrationBuilder.DropColumn(
            name: "ProviderPublishableKey",
            schema: "dbo",
            table: "CustomerPayments");
        migrationBuilder.DropColumn(
            name: "StripeIdempotencyKey",
            schema: "dbo",
            table: "CustomerPayments");

        migrationBuilder.AddColumn<string>(
            name: "CheckoutUrl",
            schema: "dbo",
            table: "CustomerPayments",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "InitializationState",
            schema: "dbo",
            table: "CustomerPayments",
            type: "nvarchar(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Legacy");
        migrationBuilder.AddColumn<string>(
            name: "Provider",
            schema: "dbo",
            table: "CustomerPayments",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Stripe");
        migrationBuilder.AddColumn<string>(
            name: "Provider",
            schema: "dbo",
            table: "PaymentWebhookEvents",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Stripe");

        migrationBuilder.Sql(
            """
            UPDATE [dbo].[CustomerPayments]
            SET [ProviderStatus] = COALESCE([ProviderStatus], 'legacy_provider_retired')
            WHERE [Status] = 0;
            """);

        migrationBuilder.AddPrimaryKey(
            name: "PK_PaymentWebhookEvents",
            schema: "dbo",
            table: "PaymentWebhookEvents",
            columns: new[] { "Provider", "EventId" });
        migrationBuilder.AddCheckConstraint(
            name: "CK_PaymentWebhookEvents_State",
            schema: "dbo",
            table: "PaymentWebhookEvents",
            sql: "[State] IN ('Processing','Completed','Quarantined','Deferred')");
        migrationBuilder.AddForeignKey(
            name: "FK_PaymentWebhookEvents_CustomerPayments_CustomerPaymentId",
            schema: "dbo",
            table: "PaymentWebhookEvents",
            column: "CustomerPaymentId",
            principalSchema: "dbo",
            principalTable: "CustomerPayments",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateIndex(
            name: "IX_CustomerPayments_InitializationLeaseExpiresAtUtc",
            schema: "dbo",
            table: "CustomerPayments",
            column: "InitializationLeaseExpiresAtUtc",
            filter: "[InitializationLeaseOwnerToken] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_CustomerPayments_Provider_ProviderReference",
            schema: "dbo",
            table: "CustomerPayments",
            columns: new[] { "Provider", "ProviderReference" },
            unique: true,
            filter: "[ProviderReference] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_CustomerPayments_Provider_ProviderTransactionId",
            schema: "dbo",
            table: "CustomerPayments",
            columns: new[] { "Provider", "ProviderTransactionId" },
            unique: true,
            filter: "[ProviderTransactionId] IS NOT NULL");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CustomerPayments_Currency",
            schema: "dbo",
            table: "CustomerPayments",
            sql: "([Provider] = 'Lahza' AND [Currency] IN ('ILS','JOD','USD')) OR " +
                 "([Provider] = 'Stripe' AND [Currency] IN ('ILS','USD','EUR'))");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CustomerPayments_InitializationState",
            schema: "dbo",
            table: "CustomerPayments",
            sql: "[InitializationState] IN ('NotStarted','Initialized','Ambiguous','Legacy')");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CustomerPayments_Provider",
            schema: "dbo",
            table: "CustomerPayments",
            sql: "[Provider] IN ('Lahza','Stripe')");
        migrationBuilder.CreateIndex(
            name: "IX_PaymentWebhookEvents_CustomerPaymentId_State_ProviderTransactionId",
            schema: "dbo",
            table: "PaymentWebhookEvents",
            columns: new[] { "CustomerPaymentId", "State", "ProviderTransactionId" });
        migrationBuilder.CreateIndex(
            name: "IX_PaymentWebhookEvents_State_CreatedAtUtc",
            schema: "dbo",
            table: "PaymentWebhookEvents",
            columns: new[] { "State", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_CustomerPayments_InitializationLeaseExpiresAtUtc",
            schema: "dbo",
            table: "CustomerPayments");
        migrationBuilder.DropIndex(
            name: "IX_CustomerPayments_Provider_ProviderReference",
            schema: "dbo",
            table: "CustomerPayments");
        migrationBuilder.DropIndex(
            name: "IX_CustomerPayments_Provider_ProviderTransactionId",
            schema: "dbo",
            table: "CustomerPayments");
        migrationBuilder.DropCheckConstraint(
            name: "CK_CustomerPayments_Currency",
            schema: "dbo",
            table: "CustomerPayments");
        migrationBuilder.DropCheckConstraint(
            name: "CK_CustomerPayments_InitializationState",
            schema: "dbo",
            table: "CustomerPayments");
        migrationBuilder.DropCheckConstraint(
            name: "CK_CustomerPayments_Provider",
            schema: "dbo",
            table: "CustomerPayments");

        migrationBuilder.DropForeignKey(
            name: "FK_PaymentWebhookEvents_CustomerPayments_CustomerPaymentId",
            schema: "dbo",
            table: "PaymentWebhookEvents");
        migrationBuilder.DropPrimaryKey(
            name: "PK_PaymentWebhookEvents",
            schema: "dbo",
            table: "PaymentWebhookEvents");
        migrationBuilder.DropCheckConstraint(
            name: "CK_PaymentWebhookEvents_State",
            schema: "dbo",
            table: "PaymentWebhookEvents");
        migrationBuilder.DropIndex(
            name: "IX_PaymentWebhookEvents_CustomerPaymentId_State_ProviderTransactionId",
            schema: "dbo",
            table: "PaymentWebhookEvents");
        migrationBuilder.DropIndex(
            name: "IX_PaymentWebhookEvents_State_CreatedAtUtc",
            schema: "dbo",
            table: "PaymentWebhookEvents");

        migrationBuilder.DropColumn(
            name: "Provider",
            schema: "dbo",
            table: "PaymentWebhookEvents");
        migrationBuilder.RenameColumn(
            name: "ProviderReference",
            schema: "dbo",
            table: "PaymentWebhookEvents",
            newName: "PaymentIntentId");
        migrationBuilder.RenameColumn(
            name: "ProviderTransactionId",
            schema: "dbo",
            table: "PaymentWebhookEvents",
            newName: "ChargeId");
        migrationBuilder.RenameTable(
            name: "PaymentWebhookEvents",
            schema: "dbo",
            newName: "StripeWebhookEvents",
            newSchema: "dbo");

        migrationBuilder.RenameColumn(
            name: "ProviderReference",
            schema: "dbo",
            table: "CustomerPayments",
            newName: "PaymentIntentId");
        migrationBuilder.RenameColumn(
            name: "ProviderTransactionId",
            schema: "dbo",
            table: "CustomerPayments",
            newName: "ChargeId");
        migrationBuilder.RenameColumn(
            name: "InitializationLeaseOwnerToken",
            schema: "dbo",
            table: "CustomerPayments",
            newName: "IntentLeaseOwnerToken");
        migrationBuilder.RenameColumn(
            name: "InitializationLeaseExpiresAtUtc",
            schema: "dbo",
            table: "CustomerPayments",
            newName: "IntentLeaseExpiresAtUtc");

        migrationBuilder.DropColumn(
            name: "CheckoutUrl",
            schema: "dbo",
            table: "CustomerPayments");
        migrationBuilder.DropColumn(
            name: "InitializationState",
            schema: "dbo",
            table: "CustomerPayments");
        migrationBuilder.DropColumn(
            name: "Provider",
            schema: "dbo",
            table: "CustomerPayments");
        migrationBuilder.AddColumn<string>(
            name: "ClientSecret",
            schema: "dbo",
            table: "CustomerPayments",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "ProviderPublishableKey",
            schema: "dbo",
            table: "CustomerPayments",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "StripeIdempotencyKey",
            schema: "dbo",
            table: "CustomerPayments",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "");
        migrationBuilder.Sql(
            """
            UPDATE [dbo].[CustomerPayments]
            SET [StripeIdempotencyKey] =
                CONCAT('legacy-payment-', REPLACE(CONVERT(varchar(36), [Id]), '-', ''));
            """);

        migrationBuilder.AddPrimaryKey(
            name: "PK_StripeWebhookEvents",
            schema: "dbo",
            table: "StripeWebhookEvents",
            column: "EventId");
        migrationBuilder.AddCheckConstraint(
            name: "CK_StripeWebhookEvents_State",
            schema: "dbo",
            table: "StripeWebhookEvents",
            sql: "[State] IN ('Processing','Completed','Quarantined','Deferred')");
        migrationBuilder.AddForeignKey(
            name: "FK_StripeWebhookEvents_CustomerPayments_CustomerPaymentId",
            schema: "dbo",
            table: "StripeWebhookEvents",
            column: "CustomerPaymentId",
            principalSchema: "dbo",
            principalTable: "CustomerPayments",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateIndex(
            name: "IX_CustomerPayments_IntentLeaseExpiresAtUtc",
            schema: "dbo",
            table: "CustomerPayments",
            column: "IntentLeaseExpiresAtUtc",
            filter: "[IntentLeaseOwnerToken] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_CustomerPayments_PaymentIntentId",
            schema: "dbo",
            table: "CustomerPayments",
            column: "PaymentIntentId",
            unique: true,
            filter: "[PaymentIntentId] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_CustomerPayments_StripeIdempotencyKey",
            schema: "dbo",
            table: "CustomerPayments",
            column: "StripeIdempotencyKey",
            unique: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_CustomerPayments_Currency",
            schema: "dbo",
            table: "CustomerPayments",
            sql: "[Currency] IN ('ILS','USD','EUR')");
        migrationBuilder.CreateIndex(
            name: "IX_StripeWebhookEvents_CustomerPaymentId_State_ChargeId",
            schema: "dbo",
            table: "StripeWebhookEvents",
            columns: new[] { "CustomerPaymentId", "State", "ChargeId" });
        migrationBuilder.CreateIndex(
            name: "IX_StripeWebhookEvents_State_CreatedAtUtc",
            schema: "dbo",
            table: "StripeWebhookEvents",
            columns: new[] { "State", "CreatedAtUtc" });
    }
}
