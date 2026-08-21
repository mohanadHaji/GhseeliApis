using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.Models;
using GhseeliApis.Services.Business;
using Microsoft.AspNetCore.WebUtilities;

namespace GhseeliApis.Tests.Support;

internal sealed class ManualTimeProvider : TimeProvider
{
    public ManualTimeProvider(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; private set; }

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan timeSpan)
    {
        UtcNow = UtcNow.Add(timeSpan);
    }

    public void SetUtcNow(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }
}

internal sealed class ScriptedBusinessApiClient : IBusinessApiClient
{
    private int _catalogSnapshotRequests;

    public Func<Guid, CancellationToken, Task<CatalogSnapshotResponse>> GetCatalogSnapshotHandler { get; set; } =
        (_, _) => throw new NotImplementedException();

    public int CatalogSnapshotRequests => _catalogSnapshotRequests;

    public Task<CatalogSnapshotResponse> GetCatalogSnapshotAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _catalogSnapshotRequests);
        return GetCatalogSnapshotHandler(companyId, cancellationToken);
    }

    public Task<ValidateAppointmentResponse> ValidateAppointmentAsync(
        ValidateAppointmentRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

internal sealed class TestAppLogger : IAppLogger
{
    public List<string> InfoMessages { get; } = [];
    public List<string> WarningMessages { get; } = [];
    public List<string> ErrorMessages { get; } = [];

    public void LogInfo(string message) => InfoMessages.Add(message);

    public void LogWarning(string message) => WarningMessages.Add(message);

    public void LogError(string message) => ErrorMessages.Add(message);

    public void LogError(string message, Exception exception) =>
        ErrorMessages.Add($"{message} ({exception.GetType().Name})");
}

internal static class CatalogTestSupport
{
    public static CatalogSnapshotResponse CreateSnapshot(
        Guid companyId,
        long version = 1,
        Guid? branchId = null,
        Guid? categoryId = null,
        Guid? offeringId = null,
        Guid? addonGroupId = null,
        Guid? addonChoiceId = null,
        string companyNameAr = "مغسلة المدينة",
        string? companyNameHe = "שטיפת העיר",
        string? companyDescriptionAr = "وصف النشاط",
        string? companyDescriptionHe = "תיאור העסק",
        string? phone = "+972500000111",
        string branchNameAr = "الفرع الرئيسي",
        string? branchNameHe = "הסניף הראשי",
        string addressAr = "الشارع الرئيسي 1",
        string? addressHe = "הרחוב הראשי 1",
        string categoryNameAr = "غسيل خارجي",
        string? categoryNameHe = "שטיפה חיצונית",
        string offeringNameAr = "غسيل سريع",
        string? offeringNameHe = "שטיפה מהירה",
        string addonGroupNameAr = "إضافات",
        string? addonGroupNameHe = "תוספות",
        string addonChoiceNameAr = "شمع",
        string? addonChoiceNameHe = "ווקס",
        bool includeServiceArea = true,
        bool globalOffering = false)
    {
        branchId ??= Guid.NewGuid();
        categoryId ??= Guid.NewGuid();
        offeringId ??= Guid.NewGuid();
        addonGroupId ??= Guid.NewGuid();
        addonChoiceId ??= Guid.NewGuid();

        return new CatalogSnapshotResponse
        {
            ContractVersion = Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract.Version,
            CatalogVersion = version,
            GeneratedAtUtc = new DateTime(2026, 8, 21, 18, 0, 0, DateTimeKind.Utc).AddMinutes(version),
            Company = new CatalogSnapshotCompany
            {
                Id = companyId,
                NameAr = companyNameAr,
                NameHe = companyNameHe,
                DescriptionAr = companyDescriptionAr,
                DescriptionHe = companyDescriptionHe,
                Phone = phone
            },
            Branches =
            [
                new CatalogSnapshotBranch
                {
                    Id = branchId.Value,
                    NameAr = branchNameAr,
                    NameHe = branchNameHe,
                    AddressAr = addressAr,
                    AddressHe = addressHe,
                    Latitude = 32.1,
                    Longitude = 34.8
                }
            ],
            ServiceAreas = includeServiceArea
                ?
                [
                    new CatalogSnapshotServiceArea
                    {
                        BranchId = branchId.Value,
                        IsActive = true,
                        UsesBranchCoordinates = true,
                        CenterLatitude = 32.1,
                        CenterLongitude = 34.8,
                        RadiusKm = 12
                    }
                ]
                : Array.Empty<CatalogSnapshotServiceArea>(),
            Categories =
            [
                new CatalogSnapshotCategory
                {
                    Id = categoryId.Value,
                    NameAr = categoryNameAr,
                    NameHe = categoryNameHe,
                    DescriptionAr = "وصف الفئة",
                    DescriptionHe = "תיאור הקטגוריה",
                    DisplayOrder = 1,
                    Offerings =
                    [
                        new CatalogSnapshotOffering
                        {
                            Id = offeringId.Value,
                            BranchId = globalOffering ? null : branchId,
                            NameAr = offeringNameAr,
                            NameHe = offeringNameHe,
                            DescriptionAr = "وصف الخدمة",
                            DescriptionHe = "תיאור השירות",
                            BasePrice = 79.5m,
                            DurationMinutes = 30,
                            ImageUrl = "https://example.test/offering.jpg",
                            ReferenceCode = "FAST-01",
                            DisplayOrder = 2,
                            AddonGroups =
                            [
                                new CatalogSnapshotAddonGroup
                                {
                                    Id = addonGroupId.Value,
                                    NameAr = addonGroupNameAr,
                                    NameHe = addonGroupNameHe,
                                    DescriptionAr = "وصف الإضافة",
                                    DescriptionHe = "תיאור התוספת",
                                    SelectionType = "Multiple",
                                    IsRequired = false,
                                    MinimumSelections = 0,
                                    MaximumSelections = 2,
                                    DisplayOrder = 3,
                                    Choices =
                                    [
                                        new CatalogSnapshotAddonChoice
                                        {
                                            Id = addonChoiceId.Value,
                                            NameAr = addonChoiceNameAr,
                                            NameHe = addonChoiceNameHe,
                                            DescriptionAr = "وصف الخيار",
                                            DescriptionHe = "תיאור הבחירה",
                                            PriceAdjustment = 9.5m,
                                            DurationAdjustmentMinutes = 5,
                                            DefaultQuantity = 0,
                                            DisplayOrder = 4
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };
    }

    public static CustomerDevice CreateDevice(
        string token,
        DateTimeOffset? expiresAt = null)
    {
        return new CustomerDevice
        {
            Id = Guid.NewGuid(),
            InstallationId = Guid.NewGuid(),
            Platform = "iOS",
            AppVersion = "1.0.0",
            TokenHash = GhseeliApis.Services.Devices.DeviceTokenHasher.Hash(token),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(30)
        };
    }

    public static string CreateToken(byte value) =>
        WebEncoders.Base64UrlEncode(Enumerable.Repeat(value, 32).ToArray());
}
