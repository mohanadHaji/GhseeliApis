using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.BusinessApi.Services.Catalog;
using Ghseeli.BusinessApi.Services.Validation.Availability;
using Ghseeli.BusinessApi.Validators.Internal;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies authoritative appointment validation behavior and stable failure codes.
/// </summary>
public class AppointmentValidationServiceTests
{
    private readonly Mock<ICatalogRepository> _catalogRepository = new();
    private readonly Mock<IAvailabilityRepository> _availabilityRepository = new();
    private readonly Mock<IAppLogger> _logger = new();
    private readonly AppointmentValidationService _service;

    public AppointmentValidationServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BusinessCatalog:Currency"] = "ILS"
            })
            .Build();

        _service = new AppointmentValidationService(
            _catalogRepository.Object,
            _availabilityRepository.Object,
            new InternalAppointmentRequestValidator(new ValidateAppointmentRequestValidator()),
            new CatalogSelectionValidator(new CatalogRuleValidator()),
            new TimeZoneAvailabilityResolver(new FakeClock(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc))),
            new ServiceAreaCalculator(),
            configuration,
            _logger.Object);
    }

    [Fact]
    public async Task ValidateAsync_WhenCatalogVersionIsStale_ReturnsStableErrorWithCurrentVersion()
    {
        var (company, branch, offering, choiceId) = CreateValidationGraph();

        _availabilityRepository.Setup(repository => repository.GetBranchWithAvailabilityAsync(branch.Id))
            .ReturnsAsync(branch);
        _catalogRepository.Setup(repository => repository.GetOfferingByIdAsync(offering.Id))
            .ReturnsAsync(offering);

        var result = await _service.ValidateAsync(new ValidateAppointmentRequest
        {
            BranchId = branch.Id,
            OfferingId = offering.Id,
            SelectedAddons =
            [
                new ValidateAppointmentAddonSelectionRequest
                {
                    AddonChoiceId = choiceId,
                    Quantity = 1
                }
            ],
            RequestedSlotStartUtc = new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc),
            CustomerLocation = new AppointmentCustomerLocationFacts
            {
                Latitude = 24.7136,
                Longitude = 46.6753
            },
            ExpectedCatalogVersion = company.CatalogVersion - 1,
            Currency = "ILS"
        });

        result.Valid.Should().BeFalse();
        result.CatalogVersion.Should().Be(company.CatalogVersion);
        result.Errors.Should().Contain(error =>
            error.Code == AppointmentValidationErrorCodes.StaleCatalogVersion);
        result.TotalPrice.Should().Be(60.01m);
        result.TotalDurationMinutes.Should().Be(60);
    }

    [Fact]
    public async Task ValidateAsync_WhenServiceAreaIsMissing_ReturnsFailClosed()
    {
        var (company, branch, offering, _) = CreateValidationGraph(includeServiceArea: false);

        _availabilityRepository.Setup(repository => repository.GetBranchWithAvailabilityAsync(branch.Id))
            .ReturnsAsync(branch);
        _catalogRepository.Setup(repository => repository.GetOfferingByIdAsync(offering.Id))
            .ReturnsAsync(offering);

        var result = await _service.ValidateAsync(new ValidateAppointmentRequest
        {
            BranchId = branch.Id,
            OfferingId = offering.Id,
            RequestedSlotStartUtc = new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc),
            Currency = "ILS"
        });

        result.Valid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.Code == AppointmentValidationErrorCodes.ServiceAreaNotConfigured);
    }

    [Fact]
    public async Task ValidateAsync_WhenRequestedSlotHasPositiveOffset_NormalizesToUtcFacts()
    {
        var (company, branch, offering, choiceId) = CreateValidationGraph();

        _availabilityRepository.Setup(repository => repository.GetBranchWithAvailabilityAsync(branch.Id))
            .ReturnsAsync(branch);
        _catalogRepository.Setup(repository => repository.GetOfferingByIdAsync(offering.Id))
            .ReturnsAsync(offering);

        var result = await _service.ValidateAsync(new ValidateAppointmentRequest
        {
            BranchId = branch.Id,
            OfferingId = offering.Id,
            SelectedAddons =
            [
                new ValidateAppointmentAddonSelectionRequest
                {
                    AddonChoiceId = choiceId,
                    Quantity = 1
                }
            ],
            RequestedSlotStartUtc = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.FromHours(3)),
            CustomerLocation = new AppointmentCustomerLocationFacts
            {
                Latitude = 24.7136,
                Longitude = 46.6753
            },
            ExpectedCatalogVersion = company.CatalogVersion,
            Currency = "ILS"
        });

        result.Valid.Should().BeTrue();
        result.Availability.RequestedSlotStartUtc.Should().Be(new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc));
        result.Availability.RequestedSlotEndUtc.Should().Be(new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ValidateAsync_WhenCompanyIsInactive_ReturnsStableInactiveIssue()
    {
        var (company, branch, offering, _) = CreateValidationGraph();
        company.IsActive = false;

        _availabilityRepository.Setup(repository => repository.GetBranchWithAvailabilityAsync(branch.Id))
            .ReturnsAsync(branch);
        _catalogRepository.Setup(repository => repository.GetOfferingByIdAsync(offering.Id))
            .ReturnsAsync(offering);

        var result = await _service.ValidateAsync(new ValidateAppointmentRequest
        {
            BranchId = branch.Id,
            OfferingId = offering.Id,
            RequestedSlotStartUtc = new DateTimeOffset(new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc)),
            Currency = "ILS",
            CustomerLocation = new AppointmentCustomerLocationFacts
            {
                Latitude = 24.7136,
                Longitude = 46.6753
            }
        });

        result.Valid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.Code == AppointmentValidationErrorCodes.CompanyInactive);
    }

    private static (Company Company, Branch Branch, ServiceOffering Offering, Guid ChoiceId) CreateValidationGraph(
        bool includeServiceArea = true)
    {
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة",
            IsActive = true,
            CatalogVersion = 12
        };
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            NameAr = "الفرع الرئيسي",
            AddressAr = "العنوان",
            Latitude = 24.7136,
            Longitude = 46.6753,
            IsActive = true
        };
        branch.AvailabilitySettings = new BranchAvailabilitySettings
        {
            BranchId = branch.Id,
            Branch = branch,
            TimeZoneId = "UTC",
            MinimumLeadMinutes = 0,
            BookingHorizonDays = 30,
            IsActive = true
        };
        branch.RecurringSchedules.Add(new BranchRecurringSchedule
        {
            BranchId = branch.Id,
            Branch = branch,
            DayOfWeek = DayOfWeek.Monday,
            StartLocalTime = TimeSpan.FromHours(9),
            EndLocalTime = TimeSpan.FromHours(18),
            SlotDurationMinutes = 15,
            Capacity = 3,
            IsActive = true
        });

        if (includeServiceArea)
        {
            branch.ServiceArea = new BranchServiceArea
            {
                BranchId = branch.Id,
                Branch = branch,
                CenterLatitude = 24.7136,
                CenterLongitude = 46.6753,
                RadiusKm = 10d,
                IsActive = true
            };
        }

        var category = new ServiceCategory
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            NameAr = "تنظيف",
            IsActive = true
        };
        var offering = new ServiceOffering
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Category = category,
            BranchId = branch.Id,
            Branch = branch,
            NameAr = "غسيل",
            BasePrice = 49.995m,
            DurationMinutes = 45,
            IsActive = true
        };
        var group = new AddonGroup
        {
            Id = Guid.NewGuid(),
            ServiceOfferingId = offering.Id,
            ServiceOffering = offering,
            NameAr = "إضافات",
            SelectionType = AddonSelectionType.QuantityCounter,
            IsRequired = false,
            MinimumSelections = 0,
            MaximumSelections = 3,
            IsActive = true
        };
        var choice = new AddonChoice
        {
            Id = Guid.NewGuid(),
            AddonGroupId = group.Id,
            AddonGroup = group,
            NameAr = "شمع",
            PriceAdjustment = 10.005m,
            DurationAdjustmentMinutes = 15,
            DefaultQuantity = 0,
            IsActive = true
        };

        group.Choices.Add(choice);
        offering.AddonGroups.Add(group);
        category.Offerings.Add(offering);

        return (company, branch, offering, choice.Id);
    }

    private sealed class FakeClock : ISystemClock
    {
        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
    }
}
