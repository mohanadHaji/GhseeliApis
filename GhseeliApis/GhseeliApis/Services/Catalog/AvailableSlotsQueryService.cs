using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Checkout;
using Microsoft.Extensions.Options;

namespace GhseeliApis.Services.Catalog;

public interface IAvailableSlotsQueryService
{
    Task<CatalogAvailableSlotsResponse> GetAsync(
        Guid businessId,
        Guid branchId,
        GetAvailableSlotsRequest request,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken);
}

public sealed class AvailableSlotsQueryService : IAvailableSlotsQueryService
{
    private readonly ICatalogReadModelService _catalogService;
    private readonly IBusinessApiClient _businessApiClient;
    private readonly CheckoutPricingOptions _pricingOptions;

    public AvailableSlotsQueryService(
        ICatalogReadModelService catalogService,
        IBusinessApiClient businessApiClient,
        IOptions<CheckoutPricingOptions> pricingOptions)
    {
        _catalogService = catalogService;
        _businessApiClient = businessApiClient;
        _pricingOptions = pricingOptions.Value;
    }

    public async Task<CatalogAvailableSlotsResponse> GetAsync(
        Guid businessId,
        Guid branchId,
        GetAvailableSlotsRequest request,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken)
    {
        var catalog = await _catalogService.GetBusinessOfferingsAsync(
            businessId,
            new GetCatalogBusinessOfferingsRequest
            {
                BranchId = branchId,
                Language = request.Language
            },
            acceptLanguageHeader,
            cancellationToken);
        var branch = catalog.Business.Branches.SingleOrDefault(item => item.Id == branchId);
        if (branch is null)
        {
            throw new CatalogReadModelException(
                CatalogProblemCodes.FilterMismatch,
                StatusCodes.Status400BadRequest,
                "The selected branch does not belong to the selected business.");
        }

        var mappedItems = new List<AvailableSlotsItemRequest>();
        foreach (var item in request.Items)
        {
            var offering = catalog.Offerings.SingleOrDefault(value => value.Id == item.OfferingId);
            if (offering is null)
            {
                throw new CatalogReadModelException(
                    CatalogProblemCodes.FilterMismatch,
                    StatusCodes.Status400BadRequest,
                    "The selected offering does not belong to the selected business and branch.");
            }

            var choices = offering.AddonGroups
                .SelectMany(group => group.Choices)
                .ToDictionary(choice => choice.Id);
            var mappedSelections = new List<ValidateAppointmentAddonSelectionRequest>();
            foreach (var selection in item.SelectedAddons)
            {
                if (!choices.TryGetValue(selection.AddonChoiceId, out var choice))
                {
                    throw new CatalogReadModelException(
                        CatalogProblemCodes.FilterMismatch,
                        StatusCodes.Status400BadRequest,
                        "The selected add-on does not belong to the selected offering.");
                }

                mappedSelections.Add(new ValidateAppointmentAddonSelectionRequest
                {
                    AddonChoiceId = choice.SourceId,
                    Quantity = selection.Quantity
                });
            }

            mappedItems.Add(new AvailableSlotsItemRequest
            {
                OfferingId = offering.SourceId,
                SelectedAddons = mappedSelections
            });
        }

        AvailableSlotsResponse response;
        var upstreamRequest = new AvailableSlotsRequest
        {
            CompanyId = catalog.Business.SourceId,
            BranchId = branch.SourceId,
            Date = request.Date,
            ExpectedCatalogVersion = request.ExpectedCatalogVersion
                ?? catalog.Business.Catalog.Version,
            Currency = _pricingOptions.Currency.Trim().ToUpperInvariant(),
            CustomerLocation = request.CustomerLocation is null
                ? null
                : new AppointmentCustomerLocationFacts
                {
                    Latitude = request.CustomerLocation.Latitude!.Value,
                    Longitude = request.CustomerLocation.Longitude!.Value
                },
            Items = mappedItems,
            IncludeUnavailable = request.IncludeUnavailable
        };
        try
        {
            response = await _businessApiClient.GetAvailableSlotsAsync(
                upstreamRequest,
                cancellationToken);
        }
        catch (BusinessApiException exception)
        {
            throw new CatalogReadModelException(
                CatalogProblemCodes.Unavailable,
                StatusCodes.Status503ServiceUnavailable,
                exception.Message);
        }

        if (!response.Valid)
        {
            var stale = response.Errors.Any(error =>
                error.Code == AppointmentValidationErrorCodes.StaleCatalogVersion);
            throw new CatalogReadModelException(
                stale
                    ? CatalogProblemCodes.StaleVersion
                    : CatalogProblemCodes.FilterMismatch,
                stale
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest,
                response.Errors.FirstOrDefault()?.Message ??
                "The available-slots request was rejected.");
        }

        ValidateResponse(response, upstreamRequest);

        return new CatalogAvailableSlotsResponse
        {
            BusinessId = businessId,
            BranchId = branchId,
            Date = response.Date,
            TimeZoneId = response.TimeZoneId,
            CatalogVersion = response.CatalogVersion,
            Currency = response.Currency,
            TotalDurationMinutes = response.TotalDurationMinutes,
            GeneratedAtUtc = response.GeneratedAtUtc,
            Slots = response.Slots.Select(slot => new CatalogAvailableSlotResponse
            {
                StartUtc = slot.StartUtc,
                EndUtc = slot.EndUtc,
                StartLocal = slot.StartLocal,
                EndLocal = slot.EndLocal,
                ConfiguredCapacity = slot.ConfiguredCapacity,
                RemainingCapacity = slot.RemainingCapacity,
                IsAvailable = slot.IsAvailable
            }).ToArray()
        };
    }

    private void ValidateResponse(
        AvailableSlotsResponse response,
        AvailableSlotsRequest request)
    {
        var invalidIdentity =
            response.ContractVersion !=
                Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract.Version ||
            response.CompanyId != request.CompanyId ||
            response.BranchId != request.BranchId ||
            response.Date != request.Date ||
            response.CatalogVersion != request.ExpectedCatalogVersion;
        var invalidSlots = response.Slots
            .Select((slot, index) => new { slot, index })
            .Any(value =>
                value.slot.EndUtc <= value.slot.StartUtc ||
                value.slot.EndLocal <= value.slot.StartLocal ||
                DateOnly.FromDateTime(value.slot.StartLocal) != request.Date ||
                value.slot.ConfiguredCapacity <= 0 ||
                value.slot.RemainingCapacity < 0 ||
                value.slot.RemainingCapacity > value.slot.ConfiguredCapacity ||
                value.slot.IsAvailable != (value.slot.RemainingCapacity > 0) ||
                (value.index > 0 &&
                 response.Slots[value.index - 1].StartUtc >= value.slot.StartUtc));

        if (invalidIdentity ||
            !string.Equals(response.Currency, _pricingOptions.Currency.Trim(),
                StringComparison.OrdinalIgnoreCase) ||
            response.TotalDurationMinutes <= 0 ||
            invalidSlots)
        {
            throw new CatalogReadModelException(
                CatalogProblemCodes.Unavailable,
                StatusCodes.Status503ServiceUnavailable,
                "The Business API returned an invalid available-slots response.");
        }
    }
}
