using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

namespace GhseeliApis.Tests.Services.Catalog;

/// <summary>
/// Defines customer-to-business slot request mapping and failure behavior.
/// </summary>
public sealed class AvailableSlotsQueryServiceTests
{
    [Fact]
    public async Task GetAsync_MapsCustomerCatalogIdsToBusinessSourceIds()
    {
        var catalog = CreateCatalog();
        AvailableSlotsRequest? captured = null;
        var client = new ScriptedBusinessApiClient
        {
            GetAvailableSlotsHandler = (request, _) =>
            {
                captured = request;
                return Task.FromResult(new AvailableSlotsResponse
                {
                    Valid = true,
                    CompanyId = catalog.Business.SourceId,
                    BranchId = catalog.Business.Branches.Single().SourceId,
                    Date = new DateOnly(2026, 9, 7),
                    TimeZoneId = "UTC",
                    CatalogVersion = 12,
                    Currency = "ILS",
                    TotalDurationMinutes = 75,
                    GeneratedAtUtc = new DateTime(2026, 9, 5, 9, 0, 0, DateTimeKind.Utc),
                    Slots =
                    [
                        new AvailableSlotResponse
                        {
                            StartUtc = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc),
                            EndUtc = new DateTime(2026, 9, 7, 10, 15, 0, DateTimeKind.Utc),
                            StartLocal = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Unspecified),
                            EndLocal = new DateTime(2026, 9, 7, 10, 15, 0, DateTimeKind.Unspecified),
                            RemainingCapacity = 2,
                            ConfiguredCapacity = 3,
                            IsAvailable = true
                        }
                    ]
                });
            }
        };
        var service = CreateService(catalog, client);
        var offering = catalog.Offerings.Single();
        var choice = offering.AddonGroups.Single().Choices.Single();

        var response = await service.GetAsync(
            catalog.Business.Id,
            catalog.Business.Branches.Single().Id,
            new GetAvailableSlotsRequest
            {
                Date = new DateOnly(2026, 9, 7),
                Items =
                [
                    new CatalogAvailableSlotsItemRequest
                    {
                        OfferingId = offering.Id,
                        SelectedAddons =
                        [
                            new CatalogAvailableSlotsSelectionRequest
                            {
                                AddonChoiceId = choice.Id,
                                Quantity = 2
                            }
                        ]
                    }
                ]
            },
            "ar",
            default);

        captured.Should().NotBeNull();
        captured!.CompanyId.Should().Be(catalog.Business.SourceId);
        captured.BranchId.Should().Be(catalog.Business.Branches.Single().SourceId);
        captured.Items.Single().OfferingId.Should().Be(offering.SourceId);
        captured.Items.Single().SelectedAddons.Single().AddonChoiceId.Should().Be(choice.SourceId);
        captured.Items.Single().SelectedAddons.Single().Quantity.Should().Be(2);
        captured.ExpectedCatalogVersion.Should().Be(12);
        response.BusinessId.Should().Be(catalog.Business.Id);
        response.BranchId.Should().Be(catalog.Business.Branches.Single().Id);
        response.Slots.Should().ContainSingle(slot => slot.RemainingCapacity == 2);
    }

    [Fact]
    public async Task GetAsync_BranchOutsideBusiness_RejectsWithoutCallingBusinessApi()
    {
        var catalog = CreateCatalog();
        var client = new ScriptedBusinessApiClient();
        var service = CreateService(catalog, client);

        var action = () => service.GetAsync(
            catalog.Business.Id,
            Guid.NewGuid(),
            ValidRequest(catalog),
            "ar",
            default);

        var exception = await action.Should().ThrowAsync<CatalogReadModelException>();
        exception.Which.Code.Should().Be(CatalogProblemCodes.FilterMismatch);
    }

    [Fact]
    public async Task GetAsync_OfferingOutsideBusiness_RejectsWithoutCallingBusinessApi()
    {
        var catalog = CreateCatalog();
        var client = new ScriptedBusinessApiClient();
        var service = CreateService(catalog, client);
        var request = ValidRequest(catalog);
        request.Items =
        [
            new CatalogAvailableSlotsItemRequest { OfferingId = Guid.NewGuid() }
        ];

        var action = () => service.GetAsync(
            catalog.Business.Id,
            catalog.Business.Branches.Single().Id,
            request,
            "ar",
            default);

        var exception = await action.Should().ThrowAsync<CatalogReadModelException>();
        exception.Which.Code.Should().Be(CatalogProblemCodes.FilterMismatch);
    }

    [Fact]
    public async Task GetAsync_StaleCatalogResponse_MapsToConflict()
    {
        var catalog = CreateCatalog();
        var client = new ScriptedBusinessApiClient
        {
            GetAvailableSlotsHandler = (_, _) => Task.FromResult(new AvailableSlotsResponse
            {
                Valid = false,
                Errors =
                [
                    new AppointmentValidationIssue
                    {
                        Code = AppointmentValidationErrorCodes.StaleCatalogVersion,
                        Message = "stale"
                    }
                ]
            })
        };
        var service = CreateService(catalog, client);

        var action = () => service.GetAsync(
            catalog.Business.Id,
            catalog.Business.Branches.Single().Id,
            ValidRequest(catalog),
            "he",
            default);

        var exception = await action.Should().ThrowAsync<CatalogReadModelException>();
        exception.Which.Code.Should().Be(CatalogProblemCodes.StaleVersion);
        exception.Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task GetAsync_ResponseIdentityMismatch_FailsClosed()
    {
        var catalog = CreateCatalog();
        var client = new ScriptedBusinessApiClient
        {
            GetAvailableSlotsHandler = (request, _) => Task.FromResult(new AvailableSlotsResponse
            {
                Valid = true,
                CompanyId = Guid.NewGuid(),
                BranchId = request.BranchId,
                Date = request.Date,
                CatalogVersion = request.ExpectedCatalogVersion!.Value,
                Currency = request.Currency,
                TotalDurationMinutes = 60
            })
        };
        var service = CreateService(catalog, client);

        var action = () => service.GetAsync(
            catalog.Business.Id,
            catalog.Business.Branches.Single().Id,
            ValidRequest(catalog),
            "ar",
            default);

        var exception = await action.Should().ThrowAsync<CatalogReadModelException>();
        exception.Which.Code.Should().Be(CatalogProblemCodes.Unavailable);
        exception.Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task GetAsync_InvalidCapacityOrSlotOrdering_FailsClosed()
    {
        var catalog = CreateCatalog();
        var client = new ScriptedBusinessApiClient
        {
            GetAvailableSlotsHandler = (request, _) => Task.FromResult(new AvailableSlotsResponse
            {
                Valid = true,
                CompanyId = request.CompanyId,
                BranchId = request.BranchId,
                Date = request.Date,
                CatalogVersion = request.ExpectedCatalogVersion!.Value,
                Currency = request.Currency,
                TotalDurationMinutes = 60,
                Slots =
                [
                    new AvailableSlotResponse
                    {
                        StartUtc = request.Date.ToDateTime(new TimeOnly(10), DateTimeKind.Utc),
                        EndUtc = request.Date.ToDateTime(new TimeOnly(11), DateTimeKind.Utc),
                        StartLocal = request.Date.ToDateTime(new TimeOnly(10)),
                        EndLocal = request.Date.ToDateTime(new TimeOnly(11)),
                        ConfiguredCapacity = 1,
                        RemainingCapacity = 2,
                        IsAvailable = true
                    }
                ]
            })
        };
        var service = CreateService(catalog, client);

        var action = () => service.GetAsync(
            catalog.Business.Id,
            catalog.Business.Branches.Single().Id,
            ValidRequest(catalog),
            "ar",
            default);

        await action.Should().ThrowAsync<CatalogReadModelException>()
            .Where(exception => exception.Code == CatalogProblemCodes.Unavailable);
    }

    private static AvailableSlotsQueryService CreateService(
        CatalogBusinessOfferingsResponse catalog,
        IBusinessApiClient client)
    {
        var catalogService = new Mock<ICatalogReadModelService>();
        catalogService.Setup(service => service.GetBusinessOfferingsAsync(
                catalog.Business.Id,
                It.IsAny<GetCatalogBusinessOfferingsRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        return new AvailableSlotsQueryService(
            catalogService.Object,
            client,
            Options.Create(new CheckoutPricingOptions { Currency = "ils" }));
    }

    private static GetAvailableSlotsRequest ValidRequest(
        CatalogBusinessOfferingsResponse catalog) => new()
    {
        Date = new DateOnly(2026, 9, 7),
        Items =
        [
            new CatalogAvailableSlotsItemRequest
            {
                OfferingId = catalog.Offerings.Single().Id
            }
        ]
    };

    private static CatalogBusinessOfferingsResponse CreateCatalog()
    {
        var businessId = Guid.NewGuid();
        var businessSourceId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        return new CatalogBusinessOfferingsResponse
        {
            Business = new CatalogBusinessResponse
            {
                Id = businessId,
                SourceId = businessSourceId,
                Catalog = new CatalogMetadataResponse { Version = 12 },
                Branches =
                [
                    new CatalogBranchResponse
                    {
                        Id = branchId,
                        SourceId = Guid.NewGuid()
                    }
                ]
            },
            Offerings =
            [
                new CatalogOfferingResponse
                {
                    Id = Guid.NewGuid(),
                    SourceId = Guid.NewGuid(),
                    AddonGroups =
                    [
                        new CatalogAddonGroupResponse
                        {
                            Id = Guid.NewGuid(),
                            SourceId = Guid.NewGuid(),
                            Choices =
                            [
                                new CatalogAddonChoiceResponse
                                {
                                    Id = Guid.NewGuid(),
                                    SourceId = Guid.NewGuid()
                                }
                            ]
                        }
                    ]
                }
            ]
        };
    }
}
