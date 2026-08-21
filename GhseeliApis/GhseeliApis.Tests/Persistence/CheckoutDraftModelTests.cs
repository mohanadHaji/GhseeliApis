using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Migrations;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GhseeliApis.Tests.Persistence;

/// <summary>
/// Verifies the persisted schema for anonymous checkout drafts.
/// </summary>
public class CheckoutDraftModelTests
{
    [Fact]
    public void Model_UsesUniqueOrderGuidOwnerLookupIndexes_AndConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new ApplicationDbContext(options);
        var draftEntity = context.Model.FindEntityType(typeof(CheckoutDraft));
        var itemEntity = context.Model.FindEntityType(typeof(CheckoutDraftItem));
        var selectionEntity = context.Model.FindEntityType(typeof(CheckoutDraftSelection));

        draftEntity.Should().NotBeNull();
        draftEntity!.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Single().Name == nameof(CheckoutDraft.OrderGuid));
        draftEntity.GetIndexes().Should().Contain(index =>
            index.Properties.Count == 2 &&
            index.Properties[0].Name == nameof(CheckoutDraft.OwnerDeviceId) &&
            index.Properties[1].Name == nameof(CheckoutDraft.OrderGuid));
        draftEntity.GetIndexes().Should().Contain(index =>
            index.Properties.Count == 2 &&
            index.Properties[0].Name == nameof(CheckoutDraft.OwnerDeviceId) &&
            index.Properties[1].Name == nameof(CheckoutDraft.ExpiresAt));
        draftEntity.FindProperty(nameof(CheckoutDraft.RowVersion))!
            .IsConcurrencyToken.Should().BeTrue();

        itemEntity!.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Count == 2 &&
            index.Properties[0].Name == nameof(CheckoutDraftItem.CheckoutDraftId) &&
            index.Properties[1].Name == nameof(CheckoutDraftItem.OfferingSourceId));
        selectionEntity!.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Count == 2 &&
            index.Properties[0].Name == nameof(CheckoutDraftSelection.CheckoutDraftItemId) &&
            index.Properties[1].Name == nameof(CheckoutDraftSelection.AddonChoiceSourceId));
    }

    [Fact]
    public void AddCheckoutDraftsMigration_CreatesUniqueDraftItemAndSelectionIndexes()
    {
        var migrationBuilder = new MigrationBuilder("SqlServer");
        new CheckoutDraftMigrationAccessor().ApplyUp(migrationBuilder);
        var indexOperations = migrationBuilder.Operations.OfType<CreateIndexOperation>().ToArray();

        indexOperations.Should().Contain(operation =>
            operation.Name == "IX_CheckoutDraftItems_CheckoutDraftId_OfferingSourceId" &&
            operation.Table == "CheckoutDraftItems" &&
            operation.IsUnique);
        indexOperations.Should().Contain(operation =>
            operation.Name == "IX_CheckoutDraftSelections_CheckoutDraftItemId_AddonChoiceSourceId" &&
            operation.Table == "CheckoutDraftSelections" &&
            operation.IsUnique);
    }

    private sealed class CheckoutDraftMigrationAccessor : AddCheckoutDrafts
    {
        public void ApplyUp(MigrationBuilder migrationBuilder) => Up(migrationBuilder);
    }
}
