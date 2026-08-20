using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.BusinessApi.Services.Validation.Availability;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Services;

public class AppointmentValidationService : IAppointmentValidationService
{
    private readonly ICompanyRepository _companyRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly IAvailabilityRepository _availabilityRepository;
    private readonly IInternalAppointmentRequestValidator _requestValidator;
    private readonly ICatalogSelectionValidator _selectionValidator;
    private readonly ITimeZoneAvailabilityResolver _availabilityResolver;
    private readonly IServiceAreaCalculator _serviceAreaCalculator;
    private readonly IConfiguration _configuration;
    private readonly IAppLogger _logger;

    public AppointmentValidationService(
        ICompanyRepository companyRepository,
        ICatalogRepository catalogRepository,
        IAvailabilityRepository availabilityRepository,
        IInternalAppointmentRequestValidator requestValidator,
        ICatalogSelectionValidator selectionValidator,
        ITimeZoneAvailabilityResolver availabilityResolver,
        IServiceAreaCalculator serviceAreaCalculator,
        IConfiguration configuration,
        IAppLogger logger)
    {
        _companyRepository = companyRepository;
        _catalogRepository = catalogRepository;
        _availabilityRepository = availabilityRepository;
        _requestValidator = requestValidator;
        _selectionValidator = selectionValidator;
        _availabilityResolver = availabilityResolver;
        _serviceAreaCalculator = serviceAreaCalculator;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ValidateAppointmentResponse> ValidateAsync(
        Guid userId,
        bool isAdmin,
        ValidateAppointmentRequest request)
    {
        _requestValidator.Validate(request);

        var issues = new List<AppointmentValidationIssue>();
        var branch = await _availabilityRepository.GetBranchWithAvailabilityAsync(request.BranchId);
        var offering = await _catalogRepository.GetOfferingByIdAsync(request.OfferingId);

        if (branch is not null)
        {
            await EnsureCompanyAccessAsync(userId, isAdmin, branch.CompanyId);
        }
        else if (offering is not null)
        {
            await EnsureCompanyAccessAsync(userId, isAdmin, offering.Category.CompanyId);
        }

        if (branch is null)
        {
            issues.Add(Issue(
                AppointmentValidationErrorCodes.BranchNotFound,
                "The requested branch was not found.",
                "branchId"));
        }

        if (offering is null)
        {
            issues.Add(Issue(
                AppointmentValidationErrorCodes.OfferingNotFound,
                "The requested offering was not found.",
                "offeringId"));
        }

        var catalogVersion = branch?.Company.CatalogVersion
            ?? offering?.Category.Company.CatalogVersion
            ?? 0L;

        var response = new ValidateAppointmentResponse
        {
            Valid = false,
            CatalogVersion = catalogVersion,
            Currency = GetAuthoritativeCurrency(),
            BranchId = request.BranchId,
            OfferingId = request.OfferingId
        };

        if (offering is null || branch is null)
        {
            response.Errors = issues;
            LogValidationOutcome(response, request);
            return response;
        }

        response.CatalogVersion = offering.Category.Company.CatalogVersion;
        response.Currency = GetAuthoritativeCurrency();

        if (!offering.Category.Company.IsActive)
        {
            issues.Add(Issue(
                AppointmentValidationErrorCodes.CompanyInactive,
                "The offering company is inactive."));
        }

        if (!branch.IsActive)
        {
            issues.Add(Issue(
                AppointmentValidationErrorCodes.BranchInactive,
                "The selected branch is inactive.",
                "branchId"));
        }

        if (!offering.Category.IsActive)
        {
            issues.Add(Issue(
                AppointmentValidationErrorCodes.CategoryInactive,
                "The offering category is inactive.",
                "offeringId"));
        }

        if (!offering.IsActive)
        {
            issues.Add(Issue(
                AppointmentValidationErrorCodes.OfferingInactive,
                "The offering is inactive.",
                "offeringId"));
        }

        if (branch.CompanyId != offering.Category.CompanyId ||
            (offering.BranchId.HasValue && offering.BranchId.Value != branch.Id))
        {
            issues.Add(Issue(
                AppointmentValidationErrorCodes.OfferingBranchMismatch,
                "The selected offering is not available for the selected branch.",
                "offeringId"));
        }

        if (!string.Equals(
                GetAuthoritativeCurrency(),
                BusinessTextNormalizer.NormalizeRequired(request.Currency),
                StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Issue(
                AppointmentValidationErrorCodes.UnsupportedCurrency,
                "The supplied currency does not match the authoritative business currency.",
                "currency"));
        }

        var selectionResult = _selectionValidator.Validate(
            offering,
            request.SelectedAddons?.ToArray() ??
            Array.Empty<ValidateAppointmentAddonSelectionRequest>());

        issues.AddRange(selectionResult.Errors);
        response.NormalizedSelections = selectionResult.NormalizedSelections;
        PopulatePricingAndDurationFacts(response, offering, selectionResult, issues);

        var serviceAreaResult = _serviceAreaCalculator.Evaluate(
            branch,
            branch.ServiceArea,
            request.CustomerLocation);
        response.ServiceArea = serviceAreaResult.Facts;
        if (!serviceAreaResult.IsValid && serviceAreaResult.Error is not null)
        {
            issues.Add(serviceAreaResult.Error);
        }

        if (response.TotalDurationMinutes > 0)
        {
            var availabilityResult = _availabilityResolver.Resolve(
                branch,
                request.RequestedSlotStartUtc.UtcDateTime,
                response.TotalDurationMinutes);
            response.Availability = availabilityResult.Facts;
            if (!availabilityResult.IsValid && availabilityResult.Error is not null)
            {
                issues.Add(availabilityResult.Error);
            }
        }
        else
        {
            response.Availability = new AppointmentAvailabilityFacts
            {
                RequestedSlotStartUtc = request.RequestedSlotStartUtc.UtcDateTime,
                RequestedSlotEndUtc = request.RequestedSlotStartUtc.UtcDateTime,
                CapacityReservationChecked = false,
                WindowSource = "None"
            };
        }

        if (request.ExpectedCatalogVersion.HasValue &&
            request.ExpectedCatalogVersion.Value != response.CatalogVersion)
        {
            issues.Add(Issue(
                AppointmentValidationErrorCodes.StaleCatalogVersion,
                "The supplied catalog version is stale.",
                "expectedCatalogVersion"));
        }

        response.Errors = issues
            .GroupBy(issue => new { issue.Code, issue.Message, issue.Field })
            .Select(group => group.First())
            .ToArray();
        response.Valid = response.Errors.Count == 0;

        LogValidationOutcome(response, request);
        return response;
    }

    private async Task EnsureCompanyAccessAsync(Guid userId, bool isAdmin, Guid companyId)
    {
        if (isAdmin)
        {
            return;
        }

        var company = await _companyRepository.GetForUserAsync(userId);
        if (company?.Id == companyId)
        {
            return;
        }

        throw new UnauthorizedAccessException(
            "The requested appointment validation resource is not assigned to this business account.");
    }

    private string GetAuthoritativeCurrency()
    {
        var configuredCurrency = _configuration["BusinessCatalog:Currency"];
        return string.IsNullOrWhiteSpace(configuredCurrency)
            ? "ILS"
            : BusinessTextNormalizer.NormalizeRequired(configuredCurrency)
                .ToUpperInvariant();
    }

    private void LogValidationOutcome(
        ValidateAppointmentResponse response,
        ValidateAppointmentRequest request)
    {
        if (response.Valid)
        {
            _logger.LogInfo(
                $"Appointment validation succeeded. BranchId={request.BranchId}, OfferingId={request.OfferingId}, CatalogVersion={response.CatalogVersion}.");
            return;
        }

        var codes = response.Errors.Count == 0
            ? "none"
            : string.Join(",", response.Errors.Select(error => error.Code));
        _logger.LogWarning(
            $"Appointment validation rejected. BranchId={request.BranchId}, OfferingId={request.OfferingId}, CatalogVersion={response.CatalogVersion}, ErrorCodes={codes}.");
    }

    private static AppointmentValidationIssue Issue(
        string code,
        string message,
        string? field = null)
    {
        return new AppointmentValidationIssue
        {
            Code = code,
            Message = message,
            Field = field
        };
    }

    private static void PopulatePricingAndDurationFacts(
        ValidateAppointmentResponse response,
        Models.ServiceOffering offering,
        Availability.CatalogSelectionValidationResult selectionResult,
        ICollection<AppointmentValidationIssue> issues)
    {
        try
        {
            if (!BusinessMoney.IsWithinSupportedRange(offering.BasePrice))
            {
                issues.Add(Issue(
                    AppointmentValidationErrorCodes.AddonSelectionRuleViolation,
                    "The selected offering exceeds the supported monetary limits.",
                    "offeringId"));
                return;
            }

            if (offering.DurationMinutes <= 0 ||
                offering.DurationMinutes > Constants.BusinessValueLimits.MaximumDurationMinutes)
            {
                issues.Add(Issue(
                    AppointmentValidationErrorCodes.AddonSelectionRuleViolation,
                    "The selected offering duration is invalid.",
                    "offeringId"));
                return;
            }

            response.BaseSubtotal = BusinessMoney.RoundToCurrency(offering.BasePrice);
            response.AddonSubtotal = selectionResult.AddonSubtotal;
            var totalPrice = checked(response.BaseSubtotal + response.AddonSubtotal);
            if (!BusinessMoney.IsWithinSupportedRange(totalPrice))
            {
                issues.Add(Issue(
                    AppointmentValidationErrorCodes.AddonSelectionRuleViolation,
                    "The requested appointment totals exceed the supported monetary limits.",
                    "selectedAddons"));
                return;
            }

            var totalDurationMinutes = checked(
                offering.DurationMinutes + selectionResult.DurationAdjustmentMinutes);
            if (totalDurationMinutes <= 0 ||
                totalDurationMinutes > Constants.BusinessValueLimits.MaximumDurationMinutes)
            {
                issues.Add(Issue(
                    AppointmentValidationErrorCodes.AddonSelectionRuleViolation,
                    "The requested appointment totals exceed the supported duration limits.",
                    "selectedAddons"));
                return;
            }

            response.TotalPrice = BusinessMoney.RoundToCurrency(totalPrice);
            response.TotalDurationMinutes = totalDurationMinutes;
        }
        catch (OverflowException)
        {
            issues.Add(Issue(
                AppointmentValidationErrorCodes.AddonSelectionRuleViolation,
                "The requested appointment totals exceed the supported numeric limits.",
                "selectedAddons"));
        }
    }
}
