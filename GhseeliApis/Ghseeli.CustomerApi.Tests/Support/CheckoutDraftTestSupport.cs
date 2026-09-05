using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Support;

internal static class CheckoutDraftTestSupport
{
    public static async Task SeedSnapshotAsync(
        ApplicationDbContext context,
        CatalogSnapshotResponse snapshot,
        int displayOrder = 0)
    {
        await ApplySnapshotAsync(context, snapshot, displayOrder);
    }

    public static async Task ApplySnapshotAsync(
        ApplicationDbContext context,
        CatalogSnapshotResponse snapshot,
        int displayOrder = 0)
    {
        var provider = await context.CatalogProviders
            .SingleOrDefaultAsync(item => item.SourceCompanyId == snapshot.Company.Id);
        var leaseToken = $"lease-{Guid.NewGuid():N}";

        if (provider is null)
        {
            provider = new CatalogProviderReadModel
            {
                Id = Guid.NewGuid(),
                SourceCompanyId = snapshot.Company.Id,
                IsEnabled = true,
                DisplayOrder = displayOrder
            };
            context.CatalogProviders.Add(provider);
        }

        provider.IsEnabled = true;
        provider.DisplayOrder = displayOrder;
        provider.RefreshLeaseToken = leaseToken;
        provider.RefreshLeaseAcquiredAtUtc = DateTimeOffset.UtcNow;
        provider.RefreshLeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5);
        await context.SaveChangesAsync();

        var repository = new CatalogReadModelRepository(context);
        await repository.ApplySnapshotAsync(
            provider.Id,
            snapshot,
            snapshotHash: $"hash-{Guid.NewGuid():N}",
            refreshedAtUtc: DateTimeOffset.UtcNow,
            leaseToken: leaseToken,
            CancellationToken.None);
    }

    public static CreateCheckoutDraftRequest CreateValidCreateRequest(
        CatalogSnapshotResponse snapshot,
        DateTimeOffset requestedSlotStartUtc)
    {
        var branch = snapshot.Branches.First();
        var offering = snapshot.Categories.Single().Offerings.First();

        return new CreateCheckoutDraftRequest
        {
            BusinessSourceId = snapshot.Company.Id,
            BranchSourceId = branch.Id,
            RequestedSlotStartUtc = requestedSlotStartUtc,
            Vehicle = new CheckoutDraftVehicleRequest
            {
                VehicleType = "Sedan",
                LicensePlate = "12-345-67",
                Make = "Toyota",
                Model = "Corolla",
                Color = "Blue"
            },
            Location = new CheckoutDraftLocationRequest
            {
                AddressLine = "الشارع الرئيسي 10",
                City = "حيفا",
                Area = "الكرمل",
                Latitude = 32.1,
                Longitude = 34.81
            },
            Items =
            [
                new CheckoutDraftItemRequest
                {
                    OfferingSourceId = offering.Id
                }
            ]
        };
    }

    public static UpdateCheckoutDraftRequest CreateValidUpdateRequest(
        CatalogSnapshotResponse snapshot,
        DateTimeOffset requestedSlotStartUtc,
        int expectedVersion = 1)
    {
        var createRequest = CreateValidCreateRequest(snapshot, requestedSlotStartUtc);

        return new UpdateCheckoutDraftRequest
        {
            ExpectedVersion = expectedVersion,
            BusinessSourceId = createRequest.BusinessSourceId,
            BranchSourceId = createRequest.BranchSourceId,
            RequestedSlotStartUtc = createRequest.RequestedSlotStartUtc,
            Vehicle = createRequest.Vehicle,
            Location = createRequest.Location,
            Items = createRequest.Items
        };
    }
}
