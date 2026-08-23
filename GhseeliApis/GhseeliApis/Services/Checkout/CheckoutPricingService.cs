using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GhseeliApis.Services.Checkout;

public interface ICheckoutPricingService
{
    Task<DirectCheckoutPricingResponse> RepriceAsync(
        CreateCheckoutDraftRequest request,
        Guid deviceId,
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken);

    Task<CheckoutDraftResponse> RepriceDraftAsync(
        Guid orderGuid,
        RepriceCheckoutDraftRequest request,
        Guid deviceId,
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken);
}

public sealed class CheckoutPricingService : ICheckoutPricingService
{
    private const int MaximumGroupsPerItem = 10;
    private const int MaximumSelectionsPerItem = 25;
    private const int MaximumTotalDurationMinutes = 1_440;
    private const double EarthRadiusKm = 6371.0088d;
    private const decimal MaximumMoneyAmount = 9_999_999_999_999_999.99m;
    private static readonly JsonSerializerOptions JsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();

    private readonly ICheckoutDraftRepository _draftRepository;
    private readonly ICatalogReadModelRepository _catalogRepository;
    private readonly ICatalogProviderRefreshCoordinator _catalogRefreshCoordinator;
    private readonly IBusinessApiClient _businessApiClient;
    private readonly ICheckoutPaymentCapabilitiesService _paymentCapabilitiesService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly CheckoutPricingOptions _options;
    private readonly IAppLogger _logger;

    public CheckoutPricingService(
        ICheckoutDraftRepository draftRepository,
        ICatalogReadModelRepository catalogRepository,
        ICatalogProviderRefreshCoordinator catalogRefreshCoordinator,
        IBusinessApiClient businessApiClient,
        ICheckoutPaymentCapabilitiesService paymentCapabilitiesService,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IOptions<CheckoutPricingOptions> options,
        IAppLogger logger)
    {
        _draftRepository = draftRepository;
        _catalogRepository = catalogRepository;
        _catalogRefreshCoordinator = catalogRefreshCoordinator;
        _businessApiClient = businessApiClient;
        _paymentCapabilitiesService = paymentCapabilitiesService;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DirectCheckoutPricingResponse> RepriceAsync(
        CreateCheckoutDraftRequest request,
        Guid deviceId,
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken)
    {
        var language = ConfigurationLanguageResolver.Resolve(requestedLanguage, acceptLanguageHeader);
        var correlationId = ResolveCorrelationId();
        var result = await RepriceWithRefreshAsync(
            request,
            requestScope: "direct",
            correlationId,
            cancellationToken);

        var response = new DirectCheckoutPricingResponse
        {
            Language = language,
            Intent = MapIntentResponse(result.Intent, result.Quote),
            Pricing = MapPricingResponse(result.Quote),
            PaymentCapabilities = _paymentCapabilitiesService.GetCapabilities()
        };

        _logger.LogInfo(
            $"Calculated direct authoritative pricing for device {deviceId:D} with {result.Quote.Items.Count} item(s).");

        return response;
    }

    public async Task<CheckoutDraftResponse> RepriceDraftAsync(
        Guid orderGuid,
        RepriceCheckoutDraftRequest request,
        Guid deviceId,
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken)
    {
        var language = ConfigurationLanguageResolver.Resolve(requestedLanguage, acceptLanguageHeader);
        var draft = await _draftRepository.GetOwnedByOrderGuidAsync(
            orderGuid,
            deviceId,
            cancellationToken);
        if (draft is null)
        {
            throw CreateDraftNotFound();
        }

        EnsureActive(draft);
        EnsureNotClaimed(draft);
        if (draft.PublicVersion != request.ExpectedVersion)
        {
            throw CreateDraftVersionConflict();
        }

        var correlationId = ResolveCorrelationId();
        var normalizedRequest = CreateRequestFromDraft(draft);
        var originalRowVersion = draft.RowVersion.ToArray();
        var result = await RepriceWithRefreshAsync(
            normalizedRequest,
            requestScope: orderGuid.ToString("N"),
            correlationId,
            cancellationToken);

        EnsureActive(draft);
        var existingSnapshot = draft.PricingSnapshot;
        var newSnapshot = CreatePricingSnapshot(draft.Id, result.Quote);
        ApplyIntentToDraft(draft, result.Intent, result.Quote);
        draft.RequiresReprice = false;
        draft.UpdatedAt = _timeProvider.GetUtcNow();
        draft.PublicVersion = checked(draft.PublicVersion + 1);
        if (existingSnapshot is not null)
        {
            draft.PricingSnapshot = null;
            _draftRepository.RemovePricingSnapshot(existingSnapshot);
        }

        draft.PricingSnapshot = newSnapshot;
        _draftRepository.AddPricingSnapshot(newSnapshot);

        try
        {
            await _draftRepository.ExecuteInTransactionAsync(
                async innerCancellationToken =>
                {
                    var persistenceState = await _draftRepository.GetPersistenceStateForUpdateAsync(
                        draft.Id,
                        innerCancellationToken);
                    if (persistenceState is null ||
                        persistenceState.PublicVersion != request.ExpectedVersion ||
                        persistenceState.ConfirmationClaimedVersion.HasValue ||
                        !persistenceState.RowVersion.SequenceEqual(originalRowVersion))
                    {
                        throw new DbUpdateConcurrencyException();
                    }

                    EnsureActive(persistenceState.ExpiresAt);
                    await _draftRepository.SaveChangesAsync(innerCancellationToken);
                },
                new CheckoutDraftPricingPersistenceExpectation(
                    draft.Id,
                    draft.PublicVersion,
                    newSnapshot.Id),
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw CreateDraftVersionConflict();
        }

        var response = CheckoutDraftService.MapResponse(draft, language);
        response.PaymentCapabilities = _paymentCapabilitiesService.GetCapabilities();

        _logger.LogInfo(
            $"Persisted authoritative pricing for draft {orderGuid:D} to version {draft.PublicVersion}.");

        return response;
    }

    private async Task<RepriceResult> RepriceWithRefreshAsync(
        CheckoutDraftMutationRequestBase request,
        string requestScope,
        string correlationId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var normalized = await ValidateAndNormalizeAsync(request, cancellationToken);
            var quote = await TryBuildAuthoritativeQuoteAsync(
                normalized,
                requestScope,
                correlationId,
                attempt,
                cancellationToken);

            if (quote is not null)
            {
                return new RepriceResult(normalized, quote);
            }

            if (attempt == 0)
            {
                await ForceRefreshProviderAsync(normalized.BusinessSourceId, cancellationToken);
            }
        }

        throw CreateUnavailable("The authoritative pricing version changed repeatedly.");
    }

    private async Task<AuthoritativeCheckoutQuote?> TryBuildAuthoritativeQuoteAsync(
        NormalizedDraftIntent normalized,
        string requestScope,
        string correlationId,
        int attempt,
        CancellationToken cancellationToken)
    {
        var itemQuotes = new List<AuthoritativeCheckoutQuoteItem>(normalized.Items.Count);

        for (var itemIndex = 0; itemIndex < normalized.Items.Count; itemIndex++)
        {
            var item = normalized.Items[itemIndex];
            ValidateAppointmentResponse response;
            try
            {
                response = await _businessApiClient.ValidateAppointmentAsync(
                    CreateBusinessValidationRequest(normalized, item),
                    CreateIdempotencyKey(correlationId, requestScope, attempt, itemIndex, normalized, item),
                    cancellationToken);
            }
            catch (BusinessApiAuthenticationException exception)
            {
                throw CreateUnavailable(
                    $"Business authentication failed for authoritative pricing. CorrelationId={exception.CorrelationId ?? correlationId}");
            }
            catch (BusinessApiConfigurationException exception)
            {
                throw CreateUnavailable(
                    $"Business pricing configuration is invalid. CorrelationId={exception.CorrelationId ?? correlationId}");
            }
            catch (BusinessApiTimeoutException exception)
            {
                throw CreateUnavailable(
                    $"Business pricing timed out. CorrelationId={exception.CorrelationId ?? correlationId}");
            }
            catch (BusinessApiUnavailableException exception)
            {
                throw CreateUnavailable(
                    $"Business pricing is unavailable. CorrelationId={exception.CorrelationId ?? correlationId}");
            }
            catch (BusinessApiConflictException exception)
            {
                throw CreateUnavailable(
                    $"Business pricing idempotency conflicted unexpectedly. CorrelationId={exception.CorrelationId ?? correlationId}");
            }
            catch (BusinessApiContractException exception)
            {
                throw CreateUnavailable(
                    $"Business pricing returned an unexpected contract response. CorrelationId={exception.CorrelationId ?? correlationId}");
            }

            if (!response.Valid)
            {
                if (response.Errors.Any(error =>
                        string.Equals(
                            error.Code,
                            AppointmentValidationErrorCodes.StaleCatalogVersion,
                            StringComparison.Ordinal)))
                {
                    _logger.LogWarning(
                        $"Business pricing reported a stale catalog version for provider {normalized.BusinessSourceId:D}.");
                    return null;
                }

                throw MapBusinessValidationFailure(response.Errors, itemIndex);
            }

            AuthoritativeCheckoutQuoteItem itemQuote;
            try
            {
                itemQuote = MapItemQuote(itemIndex, item, response);
            }
            catch (OverflowException)
            {
                throw CreateUnavailable("The authoritative pricing totals exceeded supported numeric limits.");
            }
            if (itemQuote.CatalogVersion != normalized.CatalogVersion)
            {
                _logger.LogWarning(
                    $"Business pricing returned catalog version {itemQuote.CatalogVersion} while local version {normalized.CatalogVersion} was requested.");
                return null;
            }

            if (itemQuotes.Count > 0)
            {
                if (itemQuote.CatalogVersion != itemQuotes[0].CatalogVersion)
                {
                    _logger.LogWarning(
                        $"Business pricing returned inconsistent catalog versions for provider {normalized.BusinessSourceId:D}.");
                    return null;
                }

                if (!string.Equals(itemQuote.Currency, itemQuotes[0].Currency, StringComparison.Ordinal))
                {
                    throw CreateUnavailable("The Business API returned inconsistent currencies for a single quote.");
                }
            }

            itemQuotes.Add(itemQuote);
        }

        if (itemQuotes.Count > 0 &&
            !string.Equals(itemQuotes[0].Currency, normalized.Currency, StringComparison.Ordinal))
        {
            throw CreateUnavailable("The Business API returned a currency that does not match the configured customer currency.");
        }

        int totalDurationMinutes;
        try
        {
            totalDurationMinutes = itemQuotes.Sum(item => item.TotalDurationMinutes);
        }
        catch (OverflowException)
        {
            throw CreateUnavailable("The authoritative pricing duration exceeded supported limits.");
        }

        if (totalDurationMinutes <= 0 || totalDurationMinutes > MaximumTotalDurationMinutes)
        {
            throw CreateUnavailable("The authoritative pricing duration exceeded supported limits.");
        }

        ValidateSlot(
            normalized.Branch,
            normalized.RequestedSlotStartUtc,
            totalDurationMinutes);

        try
        {
            var baseSubtotal = 0m;
            var addonSubtotal = 0m;
            foreach (var item in itemQuotes)
            {
                baseSubtotal = CheckedRoundCurrency(baseSubtotal + item.BaseSubtotal);
                addonSubtotal = CheckedRoundCurrency(addonSubtotal + item.AddonSubtotal);
            }

            var itemSubtotal = CheckedRoundCurrency(baseSubtotal + addonSubtotal);
            var serviceFee = CalculateServiceFee(itemSubtotal);
            var taxableSubtotal = _options.TaxAppliesToServiceFee
                ? CheckedRoundCurrency(itemSubtotal + serviceFee)
                : itemSubtotal;
            var tax = CalculatePercentageAmount(taxableSubtotal, _options.TaxRatePercent);
            var grandTotal = CheckedRoundCurrency(itemSubtotal + serviceFee + tax);

            return new AuthoritativeCheckoutQuote(
                itemQuotes.Count == 0 ? normalized.CatalogVersion : itemQuotes[0].CatalogVersion,
                itemQuotes.Count == 0 ? NormalizeCurrency(_options.Currency) : itemQuotes[0].Currency,
                _timeProvider.GetUtcNow(),
                baseSubtotal,
                addonSubtotal,
                itemSubtotal,
                serviceFee,
                NormalizeServiceFeeMode(_options.ServiceFee.Mode),
                CheckedRoundCurrency(_options.ServiceFee.FlatAmount),
                _options.ServiceFee.PercentageRate,
                taxableSubtotal,
                _options.TaxRatePercent,
                _options.TaxAppliesToServiceFee,
                tax,
                grandTotal,
                totalDurationMinutes,
                itemQuotes);
        }
        catch (OverflowException)
        {
            throw CreateUnavailable("The authoritative pricing totals exceeded supported numeric limits.");
        }
    }

    private async Task<NormalizedDraftIntent> ValidateAndNormalizeAsync(
        CheckoutDraftMutationRequestBase request,
        CancellationToken cancellationToken)
    {
        ValidateRequestStructure(request);

        var provider = await _catalogRepository.GetEnabledProviderBySourceCompanyIdWithGraphAsync(
            request.BusinessSourceId,
            cancellationToken);
        if (provider is null)
        {
            throw CreateSelectionInvalid("businessSourceId");
        }

        var (branch, eligibleBranchIds) = ResolveEligibleBranch(provider, request.BranchSourceId);
        if (branch is null)
        {
            throw CreateSelectionInvalid("branchSourceId");
        }

        if (RequiresAvailabilityRefresh(branch))
        {
            try
            {
                await _catalogRefreshCoordinator.EnsureProviderUsableAsync(
                    provider,
                    forceRefresh: true,
                    cancellationToken);
            }
            catch (CatalogReadModelException)
            {
                throw CreateSlotUnavailable();
            }

            provider = await _catalogRepository.GetEnabledProviderBySourceCompanyIdWithGraphAsync(
                request.BusinessSourceId,
                cancellationToken);
            if (provider is null)
            {
                throw CreateSelectionInvalid("businessSourceId");
            }

            (branch, eligibleBranchIds) = ResolveEligibleBranch(provider, request.BranchSourceId);
            if (branch is null)
            {
                throw CreateSelectionInvalid("branchSourceId");
            }
        }

        var offeringsBySourceId = provider.Categories
            .SelectMany(category => category.Offerings)
            .Where(offering => !offering.BranchId.HasValue || eligibleBranchIds.Contains(offering.BranchId.Value))
            .ToDictionary(offering => offering.SourceOfferingId);

        var normalizedItems = new List<NormalizedDraftItem>();
        var totalDurationMinutes = 0;
        var requestedItems = request.Items.ToArray();
        for (var index = 0; index < requestedItems.Length; index++)
        {
            var requestedItem = requestedItems[index];
            if (requestedItem is null)
            {
                throw CreateInvalidRequest($"items[{index}]", CheckoutDraftFieldErrorCodes.Required);
            }

            if (!offeringsBySourceId.TryGetValue(requestedItem.OfferingSourceId, out var offering) ||
                (offering.BranchId.HasValue && offering.BranchId.Value != branch.Id))
            {
                throw CreateSelectionInvalid($"items[{index}].offeringSourceId");
            }

            var normalizedSelections = NormalizeSelections(offering, requestedItem, index, out var durationAdjustment);
            totalDurationMinutes = checked(totalDurationMinutes + offering.DurationMinutes + durationAdjustment);
            if (totalDurationMinutes > MaximumTotalDurationMinutes)
            {
                throw CreateSelectionInvalid("items");
            }

            normalizedItems.Add(new NormalizedDraftItem(
                requestedItem.OfferingSourceId,
                normalizedSelections));
        }

        ValidateLocation(branch, request.Location);
        ValidateSlot(branch, request.RequestedSlotStartUtc.ToUniversalTime(), totalDurationMinutes);

        return new NormalizedDraftIntent(
            request.BusinessSourceId,
            request.BranchSourceId,
            provider.CatalogVersion,
            request.RequestedSlotStartUtc.ToUniversalTime(),
            NormalizeCurrency(_options.Currency),
            ConfigurationTextNormalizer.NormalizeRequired(request.Vehicle.VehicleType!),
            ConfigurationTextNormalizer.NormalizeOptional(request.Vehicle.LicensePlate),
            ConfigurationTextNormalizer.NormalizeOptional(request.Vehicle.Make),
            ConfigurationTextNormalizer.NormalizeOptional(request.Vehicle.Model),
            ConfigurationTextNormalizer.NormalizeOptional(request.Vehicle.Color),
            ConfigurationTextNormalizer.NormalizeRequired(request.Location.AddressLine!),
            ConfigurationTextNormalizer.NormalizeOptional(request.Location.City),
            ConfigurationTextNormalizer.NormalizeOptional(request.Location.Area),
            request.Location.Latitude,
            request.Location.Longitude,
            branch,
            normalizedItems);
    }

    private async Task ForceRefreshProviderAsync(
        Guid businessSourceId,
        CancellationToken cancellationToken)
    {
        var provider = await _catalogRepository.GetEnabledProviderBySourceCompanyIdWithGraphAsync(
            businessSourceId,
            cancellationToken);
        if (provider is null)
        {
            throw CreateSelectionInvalid("businessSourceId");
        }

        try
        {
            await _catalogRefreshCoordinator.EnsureProviderUsableAsync(
                provider,
                forceRefresh: true,
                cancellationToken);
        }
        catch (CatalogReadModelException exception)
        {
            throw CreateUnavailable($"Unable to refresh the authoritative pricing catalog: {exception.Message}");
        }
    }

    private static ValidateAppointmentRequest CreateBusinessValidationRequest(
        NormalizedDraftIntent normalized,
        NormalizedDraftItem item) =>
        new()
        {
            BranchId = normalized.BranchSourceId,
            OfferingId = item.OfferingSourceId,
            RequestedSlotStartUtc = normalized.RequestedSlotStartUtc,
            CustomerLocation = new AppointmentCustomerLocationFacts
            {
                Latitude = normalized.Latitude,
                Longitude = normalized.Longitude
            },
            ExpectedCatalogVersion = normalized.CatalogVersion,
            Currency = normalized.Currency,
            SelectedAddons = item.Selections
                .Select(selection => new ValidateAppointmentAddonSelectionRequest
                {
                    AddonChoiceId = selection.AddonChoiceSourceId,
                    Quantity = selection.Quantity
                })
                .ToArray()
        };

    private string CreateIdempotencyKey(
        string correlationId,
        string requestScope,
        int attempt,
        int itemIndex,
        NormalizedDraftIntent normalized,
        NormalizedDraftItem item)
    {
        var builder = new StringBuilder();
        builder.Append(correlationId)
            .Append('|')
            .Append(requestScope)
            .Append('|')
            .Append(attempt)
            .Append('|')
            .Append(normalized.BusinessSourceId.ToString("N"))
            .Append('|')
            .Append(normalized.BranchSourceId.ToString("N"))
            .Append('|')
            .Append(normalized.CatalogVersion)
            .Append('|')
            .Append(normalized.RequestedSlotStartUtc.ToUniversalTime().ToString("O"))
            .Append('|')
            .Append(itemIndex)
            .Append('|')
            .Append(item.OfferingSourceId.ToString("N"));

        foreach (var selection in item.Selections
                     .OrderBy(selection => selection.AddonGroupSourceId)
                     .ThenBy(selection => selection.AddonChoiceSourceId)
                     .ThenBy(selection => selection.Quantity))
        {
            builder.Append('|')
                .Append(selection.AddonGroupSourceId.ToString("N"))
                .Append(':')
                .Append(selection.AddonChoiceSourceId.ToString("N"))
                .Append(':')
                .Append(selection.Quantity);
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
        return $"reprice-{attempt}-{itemIndex}-{hash[..48]}";
    }

    private static AuthoritativeCheckoutQuoteItem MapItemQuote(
        int itemIndex,
        NormalizedDraftItem item,
        ValidateAppointmentResponse response)
    {
        var baseSubtotal = CheckedRoundCurrencyStatic(response.BaseSubtotal);
        var addonSubtotal = CheckedRoundCurrencyStatic(response.AddonSubtotal);
        var itemSubtotal = CheckedRoundCurrencyStatic(baseSubtotal + addonSubtotal);
        var selections = response.NormalizedSelections
            .Select((selection, selectionIndex) => new AuthoritativeCheckoutQuoteSelection(
                selection.AddonGroupId,
                selection.AddonChoiceId,
                selection.SelectionType,
                selection.Quantity,
                CheckedRoundCurrencyStatic(selection.UnitPriceAdjustment),
                CheckedRoundCurrencyStatic(selection.TotalPriceAdjustment),
                selection.UnitDurationAdjustmentMinutes,
                selection.TotalDurationAdjustmentMinutes,
                selection.IsDefaultApplied ||
                item.Selections.Any(candidate =>
                    candidate.AddonChoiceSourceId == selection.AddonChoiceId &&
                    candidate.IsDefaultApplied))
            {
                DisplayOrder = selectionIndex
            })
            .ToArray();

        return new AuthoritativeCheckoutQuoteItem(
            item.OfferingSourceId,
            response.CatalogVersion,
            NormalizeCurrencyStatic(response.Currency),
            baseSubtotal,
            addonSubtotal,
            itemSubtotal,
            response.TotalDurationMinutes,
            selections)
        {
            DisplayOrder = itemIndex
        };
    }

    private CheckoutPricingException MapBusinessValidationFailure(
        IReadOnlyCollection<AppointmentValidationIssue> errors,
        int itemIndex)
    {
        if (errors.Any(error =>
                string.Equals(
                    error.Code,
                    AppointmentValidationErrorCodes.UnsupportedCurrency,
                    StringComparison.Ordinal)))
        {
            throw CreateUnavailable("The Business API rejected the configured pricing currency.");
        }

        if (errors.Any(error => IsServiceAreaError(error.Code)))
        {
            return new CheckoutPricingException(
                CheckoutPricingProblemCodes.OutOfServiceArea,
                StatusCodes.Status400BadRequest,
                "The requested service location is outside the service area.",
                SingleFieldError("location", CheckoutPricingProblemCodes.OutOfServiceArea));
        }

        if (errors.Any(error => IsSlotError(error.Code)))
        {
            return new CheckoutPricingException(
                CheckoutPricingProblemCodes.SlotUnavailable,
                StatusCodes.Status400BadRequest,
                "The requested slot is unavailable.",
                SingleFieldError("requestedSlotStartUtc", CheckoutPricingProblemCodes.SlotUnavailable));
        }

        var fieldErrors = errors
            .Select(error => MapBusinessField(itemIndex, error.Code))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                field => field,
                _ => new[] { CheckoutPricingProblemCodes.SelectionInvalid },
                StringComparer.Ordinal);

        return new CheckoutPricingException(
            CheckoutPricingProblemCodes.SelectionInvalid,
            StatusCodes.Status400BadRequest,
            "The authoritative business selections are invalid or unavailable.",
            fieldErrors.Count == 0
                ? SingleFieldError("items", CheckoutPricingProblemCodes.SelectionInvalid)
                : fieldErrors);
    }

    private static string MapBusinessField(int itemIndex, string code) =>
        code switch
        {
            AppointmentValidationErrorCodes.BranchNotFound or
            AppointmentValidationErrorCodes.BranchInactive => "branchSourceId",
            AppointmentValidationErrorCodes.OfferingNotFound or
            AppointmentValidationErrorCodes.OfferingInactive or
            AppointmentValidationErrorCodes.OfferingBranchMismatch or
            AppointmentValidationErrorCodes.CategoryInactive => $"items[{itemIndex}].offeringSourceId",
            AppointmentValidationErrorCodes.DuplicateAddonChoice or
            AppointmentValidationErrorCodes.UnknownAddonChoice or
            AppointmentValidationErrorCodes.InactiveAddonChoice or
            AppointmentValidationErrorCodes.AddonSelectionRuleViolation => $"items[{itemIndex}].selections",
            _ => "items"
        };

    private static bool IsServiceAreaError(string code) =>
        code is AppointmentValidationErrorCodes.ServiceAreaNotConfigured or
            AppointmentValidationErrorCodes.CustomerLocationRequired or
            AppointmentValidationErrorCodes.OutOfServiceArea;

    private static bool IsSlotError(string code) =>
        code is AppointmentValidationErrorCodes.AvailabilityNotConfigured or
            AppointmentValidationErrorCodes.AvailabilityInactive or
            AppointmentValidationErrorCodes.InvalidTimeZone or
            AppointmentValidationErrorCodes.SlotBeforeLeadTime or
            AppointmentValidationErrorCodes.SlotBeyondHorizon or
            AppointmentValidationErrorCodes.SlotUnavailable or
            AppointmentValidationErrorCodes.SlotMisaligned;

    private decimal CalculateServiceFee(decimal itemSubtotal)
    {
        return NormalizeServiceFeeMode(_options.ServiceFee.Mode) switch
        {
            CheckoutServiceFeeMode.Flat => CheckedRoundCurrency(_options.ServiceFee.FlatAmount),
            CheckoutServiceFeeMode.Percentage => CalculatePercentageAmount(
                itemSubtotal,
                _options.ServiceFee.PercentageRate),
            _ => 0m
        };
    }

    private static decimal CalculatePercentageAmount(decimal amount, decimal ratePercent)
    {
        try
        {
            return CheckedRoundCurrencyStatic(amount * ratePercent / 100m);
        }
        catch (OverflowException)
        {
            throw;
        }
    }

    private static decimal CheckedRoundCurrencyStatic(decimal value)
    {
        var rounded = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        if (rounded is < 0m or > MaximumMoneyAmount)
        {
            throw new OverflowException("The rounded monetary value is outside supported precision bounds.");
        }

        return rounded;
    }

    private decimal CheckedRoundCurrency(decimal value) => CheckedRoundCurrencyStatic(value);

    private static string NormalizeServiceFeeMode(string? mode) =>
        string.Equals(mode, CheckoutServiceFeeMode.Flat, StringComparison.Ordinal)
            ? CheckoutServiceFeeMode.Flat
            : string.Equals(mode, CheckoutServiceFeeMode.Percentage, StringComparison.Ordinal)
                ? CheckoutServiceFeeMode.Percentage
                : CheckoutServiceFeeMode.None;

    private static string NormalizeCurrencyStatic(string currency) =>
        ConfigurationTextNormalizer.NormalizeRequired(currency).ToUpperInvariant();

    private static string NormalizeCurrency(string currency) => NormalizeCurrencyStatic(currency);

    private static CheckoutDraftIntentResponse MapIntentResponse(
        NormalizedDraftIntent intent,
        AuthoritativeCheckoutQuote quote)
    {
        return new CheckoutDraftIntentResponse
        {
            BusinessSourceId = intent.BusinessSourceId,
            BranchSourceId = intent.BranchSourceId,
            CatalogVersion = quote.CatalogVersion,
            RequestedSlotStartUtc = intent.RequestedSlotStartUtc,
            Vehicle = new CheckoutDraftVehicleResponse
            {
                VehicleType = intent.VehicleType,
                LicensePlate = intent.LicensePlate,
                Make = intent.VehicleMake,
                Model = intent.VehicleModel,
                Color = intent.VehicleColor
            },
            Location = new CheckoutDraftLocationResponse
            {
                AddressLine = intent.AddressLine,
                City = intent.City,
                Area = intent.Area,
                Latitude = intent.Latitude,
                Longitude = intent.Longitude
            },
            Items = quote.Items
                .OrderBy(item => item.DisplayOrder)
                .Select(item => new CheckoutDraftItemResponse
                {
                    OfferingSourceId = item.OfferingSourceId,
                    Selections = item.Selections
                        .OrderBy(selection => selection.DisplayOrder)
                        .Select(selection => new CheckoutDraftSelectionResponse
                        {
                            AddonGroupSourceId = selection.AddonGroupSourceId,
                            AddonChoiceSourceId = selection.AddonChoiceSourceId,
                            Quantity = selection.Quantity
                        })
                        .ToArray()
                })
                .ToArray()
        };
    }

    private static CheckoutPricingSnapshotResponse MapPricingResponse(AuthoritativeCheckoutQuote quote)
    {
        return new CheckoutPricingSnapshotResponse
        {
            CatalogVersion = quote.CatalogVersion,
            Currency = quote.Currency,
            QuotedAtUtc = quote.QuotedAtUtc,
            BaseSubtotal = quote.BaseSubtotal,
            AddonSubtotal = quote.AddonSubtotal,
            ItemSubtotal = quote.ItemSubtotal,
            ServiceFee = quote.ServiceFee,
            ServiceFeeMode = quote.ServiceFeeMode,
            ServiceFeeFlatAmount = quote.ServiceFeeFlatAmount,
            ServiceFeePercentageRate = quote.ServiceFeePercentageRate,
            TaxableSubtotal = quote.TaxableSubtotal,
            TaxRatePercent = quote.TaxRatePercent,
            TaxAppliesToServiceFee = quote.TaxAppliesToServiceFee,
            Tax = quote.Tax,
            GrandTotal = quote.GrandTotal,
            TotalDurationMinutes = quote.TotalDurationMinutes,
            Items = quote.Items
                .OrderBy(item => item.DisplayOrder)
                .Select(item => new CheckoutPricingItemSnapshotResponse
                {
                    OfferingSourceId = item.OfferingSourceId,
                    BaseSubtotal = item.BaseSubtotal,
                    AddonSubtotal = item.AddonSubtotal,
                    ItemSubtotal = item.ItemSubtotal,
                    TotalDurationMinutes = item.TotalDurationMinutes,
                    Selections = item.Selections
                        .OrderBy(selection => selection.DisplayOrder)
                        .Select(selection => new CheckoutPricingSelectionSnapshotResponse
                        {
                            AddonGroupSourceId = selection.AddonGroupSourceId,
                            AddonChoiceSourceId = selection.AddonChoiceSourceId,
                            SelectionType = selection.SelectionType,
                            Quantity = selection.Quantity,
                            UnitPriceAdjustment = selection.UnitPriceAdjustment,
                            TotalPriceAdjustment = selection.TotalPriceAdjustment,
                            UnitDurationAdjustmentMinutes = selection.UnitDurationAdjustmentMinutes,
                            TotalDurationAdjustmentMinutes = selection.TotalDurationAdjustmentMinutes,
                            IsDefaultApplied = selection.IsDefaultApplied
                        })
                        .ToArray()
                })
                .ToArray()
        };
    }

    private static CreateCheckoutDraftRequest CreateRequestFromDraft(CheckoutDraft draft) =>
        new()
        {
            BusinessSourceId = draft.BusinessSourceId,
            BranchSourceId = draft.BranchSourceId,
            RequestedSlotStartUtc = draft.RequestedSlotStartUtc,
            Vehicle = new CheckoutDraftVehicleRequest
            {
                VehicleType = draft.VehicleType,
                LicensePlate = draft.LicensePlate,
                Make = draft.VehicleMake,
                Model = draft.VehicleModel,
                Color = draft.VehicleColor
            },
            Location = new CheckoutDraftLocationRequest
            {
                AddressLine = draft.AddressLine,
                City = draft.City,
                Area = draft.Area,
                Latitude = (double)draft.Latitude,
                Longitude = (double)draft.Longitude
            },
            Items = draft.Items
                .OrderBy(item => item.DisplayOrder)
                .Select(item => new CheckoutDraftItemRequest
                {
                    OfferingSourceId = item.OfferingSourceId,
                    Selections = item.Selections
                        .OrderBy(selection => selection.DisplayOrder)
                        .Select(selection => new CheckoutDraftSelectionRequest
                        {
                            AddonChoiceSourceId = selection.AddonChoiceSourceId,
                            Quantity = selection.Quantity
                        })
                        .ToArray()
                })
                .ToArray()
        };

    private static void ApplyIntentToDraft(
        CheckoutDraft draft,
        NormalizedDraftIntent normalized,
        AuthoritativeCheckoutQuote quote)
    {
        draft.BusinessSourceId = normalized.BusinessSourceId;
        draft.BranchSourceId = normalized.BranchSourceId;
        draft.CatalogVersion = quote.CatalogVersion;
        draft.RequestedSlotStartUtc = normalized.RequestedSlotStartUtc;
        draft.VehicleType = normalized.VehicleType;
        draft.LicensePlate = normalized.LicensePlate;
        draft.VehicleMake = normalized.VehicleMake;
        draft.VehicleModel = normalized.VehicleModel;
        draft.VehicleColor = normalized.VehicleColor;
        draft.AddressLine = normalized.AddressLine;
        draft.City = normalized.City;
        draft.Area = normalized.Area;
        draft.Latitude = ToCoordinate(normalized.Latitude);
        draft.Longitude = ToCoordinate(normalized.Longitude);

        SynchronizeItems(draft, quote.Items
            .OrderBy(item => item.DisplayOrder)
            .Select(item => new NormalizedDraftItem(
                item.OfferingSourceId,
                item.Selections
                    .OrderBy(selection => selection.DisplayOrder)
                    .Select(selection => new NormalizedDraftSelection(
                        selection.AddonGroupSourceId,
                        selection.AddonChoiceSourceId,
                        selection.Quantity,
                        selection.IsDefaultApplied))
                    .ToArray()))
            .ToArray());
    }

    private static CheckoutDraftPricingSnapshot CreatePricingSnapshot(
        Guid draftId,
        AuthoritativeCheckoutQuote quote)
    {
        return new CheckoutDraftPricingSnapshot
        {
            Id = Guid.NewGuid(),
            CheckoutDraftId = draftId,
            CatalogVersion = quote.CatalogVersion,
            Currency = quote.Currency,
            QuotedAtUtc = quote.QuotedAtUtc,
            BaseSubtotal = quote.BaseSubtotal,
            AddonSubtotal = quote.AddonSubtotal,
            ItemSubtotal = quote.ItemSubtotal,
            ServiceFee = quote.ServiceFee,
            ServiceFeeMode = quote.ServiceFeeMode,
            ServiceFeeFlatAmount = quote.ServiceFeeFlatAmount,
            ServiceFeePercentageRate = quote.ServiceFeePercentageRate,
            TaxableSubtotal = quote.TaxableSubtotal,
            TaxRatePercent = quote.TaxRatePercent,
            TaxAppliesToServiceFee = quote.TaxAppliesToServiceFee,
            Tax = quote.Tax,
            GrandTotal = quote.GrandTotal,
            TotalDurationMinutes = quote.TotalDurationMinutes,
            Items = quote.Items
                .OrderBy(item => item.DisplayOrder)
                .Select(item => new CheckoutDraftPricingItemSnapshot
                {
                    Id = Guid.NewGuid(),
                    OfferingSourceId = item.OfferingSourceId,
                    DisplayOrder = item.DisplayOrder,
                    BaseSubtotal = item.BaseSubtotal,
                    AddonSubtotal = item.AddonSubtotal,
                    ItemSubtotal = item.ItemSubtotal,
                    TotalDurationMinutes = item.TotalDurationMinutes,
                    Selections = item.Selections
                        .OrderBy(selection => selection.DisplayOrder)
                        .Select(selection => new CheckoutDraftPricingSelectionSnapshot
                        {
                            Id = Guid.NewGuid(),
                            AddonGroupSourceId = selection.AddonGroupSourceId,
                            AddonChoiceSourceId = selection.AddonChoiceSourceId,
                            SelectionType = selection.SelectionType,
                            Quantity = selection.Quantity,
                            UnitPriceAdjustment = selection.UnitPriceAdjustment,
                            TotalPriceAdjustment = selection.TotalPriceAdjustment,
                            UnitDurationAdjustmentMinutes = selection.UnitDurationAdjustmentMinutes,
                            TotalDurationAdjustmentMinutes = selection.TotalDurationAdjustmentMinutes,
                            IsDefaultApplied = selection.IsDefaultApplied,
                            DisplayOrder = selection.DisplayOrder
                        })
                        .ToArray()
                })
                .ToArray()
        };
    }

    private static void SynchronizeItems(CheckoutDraft draft, IReadOnlyList<NormalizedDraftItem> normalizedItems)
    {
        var existingItemsByOffering = draft.Items.ToDictionary(item => item.OfferingSourceId);
        var normalizedOfferings = normalizedItems
            .Select(item => item.OfferingSourceId)
            .ToHashSet();
        var reusableItems = new Queue<CheckoutDraftItem>(draft.Items
            .Where(item => !normalizedOfferings.Contains(item.OfferingSourceId))
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Id));
        var synchronizedItems = new HashSet<CheckoutDraftItem>();

        for (var itemIndex = 0; itemIndex < normalizedItems.Count; itemIndex++)
        {
            var normalizedItem = normalizedItems[itemIndex];
            if (!existingItemsByOffering.TryGetValue(normalizedItem.OfferingSourceId, out var draftItem))
            {
                if (reusableItems.Count > 0)
                {
                    draftItem = reusableItems.Dequeue();
                }
                else
                {
                    draftItem = new CheckoutDraftItem
                    {
                        Id = Guid.NewGuid()
                    };
                    draft.Items.Add(draftItem);
                }
            }

            draftItem.OfferingSourceId = normalizedItem.OfferingSourceId;
            draftItem.DisplayOrder = itemIndex;
            SynchronizeSelections(draftItem, normalizedItem.Selections);
            synchronizedItems.Add(draftItem);
        }

        foreach (var itemToRemove in draft.Items
                     .Where(item => !synchronizedItems.Contains(item))
                     .ToArray())
        {
            foreach (var selectionToRemove in itemToRemove.Selections.ToArray())
            {
                itemToRemove.Selections.Remove(selectionToRemove);
            }

            draft.Items.Remove(itemToRemove);
        }
    }

    private static void SynchronizeSelections(
        CheckoutDraftItem draftItem,
        IReadOnlyList<NormalizedDraftSelection> normalizedSelections)
    {
        var existingSelectionsByChoice = draftItem.Selections.ToDictionary(
            selection => selection.AddonChoiceSourceId);
        var normalizedChoices = normalizedSelections
            .Select(selection => selection.AddonChoiceSourceId)
            .ToHashSet();
        var reusableSelections = new Queue<CheckoutDraftSelection>(draftItem.Selections
            .Where(selection => !normalizedChoices.Contains(selection.AddonChoiceSourceId))
            .OrderBy(selection => selection.DisplayOrder)
            .ThenBy(selection => selection.Id));
        var synchronizedSelections = new HashSet<CheckoutDraftSelection>();

        for (var selectionIndex = 0; selectionIndex < normalizedSelections.Count; selectionIndex++)
        {
            var normalizedSelection = normalizedSelections[selectionIndex];
            if (!existingSelectionsByChoice.TryGetValue(
                    normalizedSelection.AddonChoiceSourceId,
                    out var draftSelection))
            {
                if (reusableSelections.Count > 0)
                {
                    draftSelection = reusableSelections.Dequeue();
                }
                else
                {
                    draftSelection = new CheckoutDraftSelection
                    {
                        Id = Guid.NewGuid()
                    };
                    draftItem.Selections.Add(draftSelection);
                }
            }

            draftSelection.AddonGroupSourceId = normalizedSelection.AddonGroupSourceId;
            draftSelection.AddonChoiceSourceId = normalizedSelection.AddonChoiceSourceId;
            draftSelection.Quantity = normalizedSelection.Quantity;
            draftSelection.DisplayOrder = selectionIndex;
            synchronizedSelections.Add(draftSelection);
        }

        foreach (var selectionToRemove in draftItem.Selections
                     .Where(selection => !synchronizedSelections.Contains(selection))
                     .ToArray())
        {
            draftItem.Selections.Remove(selectionToRemove);
        }
    }

    private static IReadOnlyList<NormalizedDraftSelection> NormalizeSelections(
        CatalogOfferingReadModel offering,
        CheckoutDraftItemRequest requestItem,
        int itemIndex,
        out int durationAdjustmentMinutes)
    {
        if (requestItem.Selections is null)
        {
            throw CreateInvalidRequest(
                $"items[{itemIndex}].selections",
                CheckoutDraftFieldErrorCodes.CollectionRequired);
        }

        if (requestItem.Selections.Count > MaximumSelectionsPerItem)
        {
            throw CreateSelectionInvalid($"items[{itemIndex}].selections");
        }

        var choiceLookup = offering.AddonGroups
            .OrderBy(group => group.DisplayOrder)
            .ThenBy(group => group.NameAr)
            .SelectMany(group => group.Choices.Select(choice => new ChoiceContext(group, choice)))
            .ToDictionary(context => context.Choice.SourceAddonChoiceId);

        var effectiveSelections = new Dictionary<Guid, (ChoiceContext Context, int Quantity)>();
        foreach (var choiceContext in choiceLookup.Values.Where(context => context.Choice.DefaultQuantity > 0))
        {
            effectiveSelections[choiceContext.Choice.SourceAddonChoiceId] =
                (choiceContext, choiceContext.Choice.DefaultQuantity);
        }

        var requestedSelections = requestItem.Selections.ToArray();
        var requestedChoiceIds = requestedSelections
            .Where(selection => selection is not null)
            .Select(selection => selection!.AddonChoiceSourceId)
            .ToHashSet();
        for (var selectionIndex = 0; selectionIndex < requestedSelections.Length; selectionIndex++)
        {
            var selection = requestedSelections[selectionIndex];
            if (selection is null)
            {
                throw CreateInvalidRequest(
                    $"items[{itemIndex}].selections[{selectionIndex}]",
                    CheckoutDraftFieldErrorCodes.Required);
            }

            if (!choiceLookup.TryGetValue(selection.AddonChoiceSourceId, out var choiceContext))
            {
                throw CreateSelectionInvalid($"items[{itemIndex}].selections");
            }

            if (NormalizeSelectionType(choiceContext.Group.SelectionType) == CatalogSelectionType.FixedIncludedChoice &&
                (choiceContext.Choice.DefaultQuantity <= 0 ||
                 selection.Quantity != choiceContext.Choice.DefaultQuantity))
            {
                throw CreateSelectionInvalid($"items[{itemIndex}].selections");
            }

            effectiveSelections[selection.AddonChoiceSourceId] = (choiceContext, selection.Quantity);
        }

        var selectedGroupCount = effectiveSelections.Values
            .Where(value => value.Quantity > 0)
            .Select(value => value.Context.Group.SourceAddonGroupId)
            .Distinct()
            .Count();
        if (selectedGroupCount > MaximumGroupsPerItem)
        {
            throw CreateInvalidRequest(
                $"items[{itemIndex}].selections",
                CheckoutDraftFieldErrorCodes.CollectionTooMany);
        }

        var normalizedSelections = new List<NormalizedDraftSelection>();
        durationAdjustmentMinutes = 0;

        foreach (var group in offering.AddonGroups
                     .OrderBy(item => item.DisplayOrder)
                     .ThenBy(item => item.NameAr))
        {
            var selectionsForGroup = group.Choices
                .OrderBy(choice => choice.DisplayOrder)
                .ThenBy(choice => choice.NameAr)
                .Select(choice =>
                {
                    effectiveSelections.TryGetValue(choice.SourceAddonChoiceId, out var value);
                    return new GroupSelectionState(choice, value.Quantity);
                })
                .Where(state => state.Quantity > 0)
                .ToArray();

            if (!ValidateGroupSelection(group, selectionsForGroup))
            {
                throw CreateSelectionInvalid($"items[{itemIndex}].selections");
            }

            foreach (var state in selectionsForGroup)
            {
                normalizedSelections.Add(new NormalizedDraftSelection(
                    group.SourceAddonGroupId,
                    state.Choice.SourceAddonChoiceId,
                    state.Quantity,
                    !requestedChoiceIds.Contains(state.Choice.SourceAddonChoiceId) &&
                    state.Choice.DefaultQuantity > 0));

                durationAdjustmentMinutes = checked(
                    durationAdjustmentMinutes + (state.Choice.DurationAdjustmentMinutes * state.Quantity));
            }
        }

        return normalizedSelections;
    }

    private static bool ValidateGroupSelection(
        CatalogAddonGroupReadModel group,
        IReadOnlyCollection<GroupSelectionState> selectedChoices)
    {
        var selectedCount = selectedChoices.Count;
        var quantitySum = selectedChoices.Sum(item => item.Quantity);

        return NormalizeSelectionType(group.SelectionType) switch
        {
            CatalogSelectionType.SingleChoice or CatalogSelectionType.SegmentedSingleButtonChoice =>
                !selectedChoices.Any(item => item.Quantity > 1) &&
                selectedCount >= group.MinimumSelections &&
                (!group.MaximumSelections.HasValue || selectedCount <= group.MaximumSelections.Value),
            CatalogSelectionType.MultipleChoice =>
                !selectedChoices.Any(item => item.Quantity > 1) &&
                selectedCount >= group.MinimumSelections &&
                (!group.MaximumSelections.HasValue || selectedCount <= group.MaximumSelections.Value),
            CatalogSelectionType.QuantityCounter =>
                quantitySum >= group.MinimumSelections &&
                (!group.MaximumSelections.HasValue || quantitySum <= group.MaximumSelections.Value),
            CatalogSelectionType.FixedIncludedChoice =>
                selectedCount == 1 && quantitySum == 1,
            _ => false
        };
    }

    private void ValidateLocation(
        CatalogBranchReadModel branch,
        CheckoutDraftLocationRequest location)
    {
        if (!branch.ServiceAreaRadiusKm.HasValue)
        {
            throw CreateSelectionInvalid("branchSourceId");
        }

        var (centerLatitude, centerLongitude) = ResolveServiceAreaCenter(branch);
        if (!centerLatitude.HasValue || !centerLongitude.HasValue)
        {
            throw CreateSelectionInvalid("branchSourceId");
        }

        var distanceKm = CalculateDistanceKm(
            centerLatitude.Value,
            centerLongitude.Value,
            location.Latitude,
            location.Longitude);

        if (distanceKm > branch.ServiceAreaRadiusKm.Value)
        {
            throw new CheckoutPricingException(
                CheckoutPricingProblemCodes.OutOfServiceArea,
                StatusCodes.Status400BadRequest,
                "The requested service location is outside the service area.",
                SingleFieldError("location", CheckoutPricingProblemCodes.OutOfServiceArea));
        }
    }

    private void ValidateSlot(
        CatalogBranchReadModel branch,
        DateTimeOffset requestedSlotStartUtc,
        int totalDurationMinutes)
    {
        var availability = DeserializeAvailability(branch);
        if (availability is null ||
            !availability.IsActive ||
            string.IsNullOrWhiteSpace(availability.TimeZoneId) ||
            totalDurationMinutes <= 0)
        {
            throw CreateSlotUnavailable();
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(availability.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw CreateSlotUnavailable();
        }
        catch (InvalidTimeZoneException)
        {
            throw CreateSlotUnavailable();
        }

        var normalizedStartUtc = requestedSlotStartUtc.UtcDateTime;
        DateTime normalizedEndUtc;
        try
        {
            normalizedEndUtc = normalizedStartUtc.AddMinutes(totalDurationMinutes);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw CreateSlotUnavailable();
        }

        var localStart = AsUnspecified(TimeZoneInfo.ConvertTimeFromUtc(normalizedStartUtc, timeZone));
        var localEnd = AsUnspecified(TimeZoneInfo.ConvertTimeFromUtc(normalizedEndUtc, timeZone));
        var actualDuration = normalizedEndUtc - normalizedStartUtc;

        if (timeZone.IsAmbiguousTime(localStart) ||
            timeZone.IsAmbiguousTime(localEnd) ||
            localEnd - localStart != actualDuration)
        {
            throw CreateSlotUnavailable();
        }

        var earliestStartUtc = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(availability.MinimumLeadMinutes);
        if (normalizedStartUtc < earliestStartUtc)
        {
            throw CreateSlotUnavailable();
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(_timeProvider.GetUtcNow().UtcDateTime, timeZone);
        if (localStart.Date > localNow.Date.AddDays(availability.BookingHorizonDays))
        {
            throw CreateSlotUnavailable();
        }

        var localStartDate = DateOnly.FromDateTime(localStart);
        var overrideForDate = availability.AvailabilityOverrides
            .SingleOrDefault(item => item.OverrideDate == localStartDate);
        IReadOnlyCollection<WindowOccurrence> windows = overrideForDate is not null
            ? GetWindowsFromOverride(localStartDate, overrideForDate)
            : GetWindowsForDate(availability, localStartDate)
                .Concat(GetCarryOverWindows(availability, localStartDate.AddDays(-1)))
                .OrderBy(window => window.StartLocalDateTime)
                .ThenBy(window => window.EndLocalDateTime)
                .ToArray();

        if (windows.Count == 0)
        {
            throw CreateSlotUnavailable();
        }

        var misaligned = false;
        foreach (var window in windows)
        {
            if (localStart < window.StartLocalDateTime ||
                localEnd > window.EndLocalDateTime)
            {
                continue;
            }

            var slotDuration = TimeSpan.FromMinutes(window.SlotDurationMinutes);
            var startOffset = localStart - window.StartLocalDateTime;
            var appointmentDuration = localEnd - localStart;
            if (appointmentDuration.Ticks % slotDuration.Ticks != 0 ||
                startOffset.Ticks % slotDuration.Ticks != 0)
            {
                misaligned = true;
                continue;
            }

            return;
        }

        if (misaligned)
        {
            throw CreateSlotUnavailable();
        }

        throw CreateSlotUnavailable();
    }

    private static CatalogSnapshotBranchAvailability? DeserializeAvailability(CatalogBranchReadModel branch)
    {
        if (string.IsNullOrWhiteSpace(branch.AvailabilitySnapshotJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CatalogSnapshotBranchAvailability>(
                branch.AvailabilitySnapshotJson,
                JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyCollection<WindowOccurrence> GetWindowsFromOverride(
        DateOnly date,
        CatalogSnapshotAvailabilityOverride availabilityOverride)
    {
        if (availabilityOverride.IsClosed ||
            !availabilityOverride.StartLocalTime.HasValue ||
            !availabilityOverride.EndLocalTime.HasValue ||
            !availabilityOverride.SlotDurationMinutes.HasValue)
        {
            return Array.Empty<WindowOccurrence>();
        }

        return
        [
            CreateOccurrence(
                date,
                availabilityOverride.StartLocalTime.Value,
                availabilityOverride.EndLocalTime.Value,
                availabilityOverride.SlotDurationMinutes.Value)
        ];
    }

    private static IReadOnlyCollection<WindowOccurrence> GetWindowsForDate(
        CatalogSnapshotBranchAvailability availability,
        DateOnly date)
    {
        var overrideForDate = availability.AvailabilityOverrides
            .SingleOrDefault(item => item.OverrideDate == date);
        if (overrideForDate is not null)
        {
            return GetWindowsFromOverride(date, overrideForDate);
        }

        return availability.RecurringSchedules
            .Where(schedule => schedule.DayOfWeek == date.DayOfWeek)
            .OrderBy(schedule => schedule.StartLocalTime)
            .Select(schedule => CreateOccurrence(
                date,
                schedule.StartLocalTime,
                schedule.EndLocalTime,
                schedule.SlotDurationMinutes))
            .ToArray();
    }

    private static IReadOnlyCollection<WindowOccurrence> GetCarryOverWindows(
        CatalogSnapshotBranchAvailability availability,
        DateOnly date)
    {
        return GetWindowsForDate(availability, date)
            .Where(window => window.EndLocalDateTime.Date > window.StartLocalDateTime.Date)
            .ToArray();
    }

    private static WindowOccurrence CreateOccurrence(
        DateOnly date,
        TimeSpan startLocalTime,
        TimeSpan endLocalTime,
        int slotDurationMinutes)
    {
        var start = date.ToDateTime(TimeOnly.FromTimeSpan(startLocalTime));
        var endDate = endLocalTime > startLocalTime ? date : date.AddDays(1);
        var end = endDate.ToDateTime(TimeOnly.FromTimeSpan(endLocalTime));

        return new WindowOccurrence(start, end, slotDurationMinutes);
    }

    private static (double? Latitude, double? Longitude) ResolveServiceAreaCenter(CatalogBranchReadModel branch)
    {
        if (branch.ServiceAreaCenterLatitude.HasValue && branch.ServiceAreaCenterLongitude.HasValue)
        {
            return (branch.ServiceAreaCenterLatitude, branch.ServiceAreaCenterLongitude);
        }

        return (branch.Latitude, branch.Longitude);
    }

    private static double CalculateDistanceKm(
        double startLatitude,
        double startLongitude,
        double endLatitude,
        double endLongitude)
    {
        var deltaLatitude = DegreesToRadians(endLatitude - startLatitude);
        var deltaLongitude = DegreesToRadians(endLongitude - startLongitude);
        var startLatitudeRadians = DegreesToRadians(startLatitude);
        var endLatitudeRadians = DegreesToRadians(endLatitude);

        var haversine = Math.Pow(Math.Sin(deltaLatitude / 2d), 2d)
            + Math.Cos(startLatitudeRadians)
            * Math.Cos(endLatitudeRadians)
            * Math.Pow(Math.Sin(deltaLongitude / 2d), 2d);

        var centralAngle = 2d * Math.Asin(Math.Min(1d, Math.Sqrt(haversine)));
        return EarthRadiusKm * centralAngle;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);

    private static DateTime AsUnspecified(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static bool IsEligibleBranch(CatalogBranchReadModel branch) =>
        branch.HasPublishedServiceArea && branch.ServiceAreaRadiusKm.HasValue;

    private static void ValidateRequestStructure(CheckoutDraftMutationRequestBase request)
    {
        if (request.Vehicle is null)
        {
            throw CreateInvalidRequest("vehicle", CheckoutDraftFieldErrorCodes.Required);
        }

        if (request.Location is null)
        {
            throw CreateInvalidRequest("location", CheckoutDraftFieldErrorCodes.Required);
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw CreateInvalidRequest("items", CheckoutDraftFieldErrorCodes.CollectionRequired);
        }
    }

    private static (CatalogBranchReadModel? Branch, HashSet<Guid> EligibleBranchIds) ResolveEligibleBranch(
        CatalogProviderReadModel provider,
        Guid branchSourceId)
    {
        var eligibleBranchIds = provider.Branches
            .Where(IsEligibleBranch)
            .Select(branch => branch.Id)
            .ToHashSet();
        var branch = provider.Branches
            .SingleOrDefault(item =>
                item.SourceBranchId == branchSourceId &&
                eligibleBranchIds.Contains(item.Id));

        return (branch, eligibleBranchIds);
    }

    private static bool RequiresAvailabilityRefresh(CatalogBranchReadModel branch) =>
        DeserializeAvailability(branch) is null;

    private void EnsureActive(CheckoutDraft draft)
    {
        EnsureActive(draft.ExpiresAt);
    }

    private static void EnsureNotClaimed(CheckoutDraft draft)
    {
        if (draft.ConfirmationClaimedVersion.HasValue)
        {
            throw CreateDraftVersionConflict();
        }
    }

    private void EnsureActive(DateTimeOffset expiresAt)
    {
        if (expiresAt <= _timeProvider.GetUtcNow())
        {
            throw new CheckoutPricingException(
                CheckoutDraftProblemCodes.Expired,
                StatusCodes.Status410Gone,
                "The checkout draft has expired.");
        }
    }

    private static decimal ToCoordinate(double value) =>
        decimal.Round((decimal)value, 6, MidpointRounding.AwayFromZero);

    private static CatalogSelectionType NormalizeSelectionType(string value) =>
        value switch
        {
            "SingleChoice" => CatalogSelectionType.SingleChoice,
            "MultipleChoice" or "Multiple" => CatalogSelectionType.MultipleChoice,
            "QuantityCounter" => CatalogSelectionType.QuantityCounter,
            "FixedIncludedChoice" => CatalogSelectionType.FixedIncludedChoice,
            "SegmentedSingleButtonChoice" => CatalogSelectionType.SegmentedSingleButtonChoice,
            _ => CatalogSelectionType.Unknown
        };

    private static CheckoutPricingException CreateSelectionInvalid(string field) =>
        new(
            CheckoutPricingProblemCodes.SelectionInvalid,
            StatusCodes.Status400BadRequest,
            "The supplied catalog selections are invalid or unavailable.",
            SingleFieldError(field, CheckoutPricingProblemCodes.SelectionInvalid));

    private static CheckoutPricingException CreateInvalidRequest(string field, string fieldCode) =>
        new(
            CheckoutPricingProblemCodes.Invalid,
            StatusCodes.Status400BadRequest,
            "The pricing request is invalid.",
            SingleFieldError(field, fieldCode));

    private static CheckoutPricingException CreateSlotUnavailable() =>
        new(
            CheckoutPricingProblemCodes.SlotUnavailable,
            StatusCodes.Status400BadRequest,
            "The requested slot is unavailable.",
            SingleFieldError("requestedSlotStartUtc", CheckoutPricingProblemCodes.SlotUnavailable));

    private static CheckoutPricingException CreateUnavailable(string message) =>
        new(
            CheckoutPricingProblemCodes.Unavailable,
            StatusCodes.Status503ServiceUnavailable,
            message);

    private static CheckoutPricingException CreateDraftNotFound() =>
        new(
            CheckoutDraftProblemCodes.NotFound,
            StatusCodes.Status404NotFound,
            "The checkout draft was not found.");

    private static CheckoutPricingException CreateDraftVersionConflict() =>
        new(
            CheckoutDraftProblemCodes.VersionConflict,
            StatusCodes.Status409Conflict,
            "The checkout draft version does not match the current record.",
            SingleFieldError("expectedVersion", CheckoutDraftProblemCodes.VersionConflict));

    private static IReadOnlyDictionary<string, string[]> SingleFieldError(string field, string code) =>
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = [code]
        };

    private string ResolveCorrelationId()
    {
        var currentCorrelationId = _httpContextAccessor.HttpContext?
            .Request
            .Headers[InternalServiceWireConstants.CorrelationIdHeaderName]
            .ToString();
        return InternalServiceHeaderValueValidator.GetOrCreateCorrelationId(currentCorrelationId);
    }

    private sealed record ChoiceContext(
        CatalogAddonGroupReadModel Group,
        CatalogAddonChoiceReadModel Choice);

    private sealed record GroupSelectionState(
        CatalogAddonChoiceReadModel Choice,
        int Quantity);

    private sealed record NormalizedDraftSelection(
        Guid AddonGroupSourceId,
        Guid AddonChoiceSourceId,
        int Quantity,
        bool IsDefaultApplied);

    private sealed record NormalizedDraftItem(
        Guid OfferingSourceId,
        IReadOnlyList<NormalizedDraftSelection> Selections);

    private sealed record NormalizedDraftIntent(
        Guid BusinessSourceId,
        Guid BranchSourceId,
        long CatalogVersion,
        DateTimeOffset RequestedSlotStartUtc,
        string Currency,
        string VehicleType,
        string? LicensePlate,
        string? VehicleMake,
        string? VehicleModel,
        string? VehicleColor,
        string AddressLine,
        string? City,
        string? Area,
        double Latitude,
        double Longitude,
        CatalogBranchReadModel Branch,
        IReadOnlyList<NormalizedDraftItem> Items);

    private sealed record RepriceResult(
        NormalizedDraftIntent Intent,
        AuthoritativeCheckoutQuote Quote);

    private sealed record AuthoritativeCheckoutQuote(
        long CatalogVersion,
        string Currency,
        DateTimeOffset QuotedAtUtc,
        decimal BaseSubtotal,
        decimal AddonSubtotal,
        decimal ItemSubtotal,
        decimal ServiceFee,
        string ServiceFeeMode,
        decimal ServiceFeeFlatAmount,
        decimal ServiceFeePercentageRate,
        decimal TaxableSubtotal,
        decimal TaxRatePercent,
        bool TaxAppliesToServiceFee,
        decimal Tax,
        decimal GrandTotal,
        int TotalDurationMinutes,
        IReadOnlyList<AuthoritativeCheckoutQuoteItem> Items);

    private sealed record AuthoritativeCheckoutQuoteItem(
        Guid OfferingSourceId,
        long CatalogVersion,
        string Currency,
        decimal BaseSubtotal,
        decimal AddonSubtotal,
        decimal ItemSubtotal,
        int TotalDurationMinutes,
        IReadOnlyList<AuthoritativeCheckoutQuoteSelection> Selections)
    {
        public int DisplayOrder { get; init; }
    }

    private sealed record AuthoritativeCheckoutQuoteSelection(
        Guid AddonGroupSourceId,
        Guid AddonChoiceSourceId,
        string SelectionType,
        int Quantity,
        decimal UnitPriceAdjustment,
        decimal TotalPriceAdjustment,
        int UnitDurationAdjustmentMinutes,
        int TotalDurationAdjustmentMinutes,
        bool IsDefaultApplied)
    {
        public int DisplayOrder { get; init; }
    }

    private sealed record WindowOccurrence(
        DateTime StartLocalDateTime,
        DateTime EndLocalDateTime,
        int SlotDurationMinutes);

    private enum CatalogSelectionType
    {
        Unknown = 0,
        SingleChoice = 1,
        MultipleChoice = 2,
        QuantityCounter = 3,
        FixedIncludedChoice = 4,
        SegmentedSingleButtonChoice = 5
    }
}
