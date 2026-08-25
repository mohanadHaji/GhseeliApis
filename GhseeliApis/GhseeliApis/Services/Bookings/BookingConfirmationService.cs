using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.DTOs.Booking;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GhseeliApis.Services.Bookings;

public interface IBookingConfirmationService
{
    Task<ConfirmedBookingResponse> ConfirmAsync(
        Guid orderGuid,
        ConfirmBookingFromDraftRequest request,
        Guid deviceId,
        Guid userId,
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken);
}

public sealed class BookingConfirmationException : Exception
{
    public BookingConfirmationException(
        string code,
        int statusCode,
        string message,
        string? businessErrorCode = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        BusinessErrorCode = businessErrorCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public string? BusinessErrorCode { get; }
}

public sealed class BookingConfirmationService : IBookingConfirmationService
{
    private static readonly JsonSerializerOptions JsonOptions =
        Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract.CreateJsonSerializerOptions();
    private readonly ApplicationDbContext _context;
    private readonly ICheckoutDraftRepository _draftRepository;
    private readonly ICatalogReadModelRepository _catalogRepository;
    private readonly IBusinessApiClient _businessApiClient;
    private readonly TimeProvider _timeProvider;
    private readonly IAppLogger _logger;

    public BookingConfirmationService(
        ApplicationDbContext context,
        ICheckoutDraftRepository draftRepository,
        ICatalogReadModelRepository catalogRepository,
        IBusinessApiClient businessApiClient,
        TimeProvider timeProvider,
        IAppLogger logger)
    {
        _context = context;
        _draftRepository = draftRepository;
        _catalogRepository = catalogRepository;
        _businessApiClient = businessApiClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ConfirmedBookingResponse> ConfirmAsync(
        Guid orderGuid,
        ConfirmBookingFromDraftRequest request,
        Guid deviceId,
        Guid userId,
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken)
    {
        var language = ConfigurationLanguageResolver.Resolve(
            requestedLanguage,
            acceptLanguageHeader);
        var existing = await LoadBookingAsync(orderGuid, cancellationToken);
        if (existing is not null)
        {
            if (existing.UserId != userId || existing.OwnerDeviceId != deviceId)
            {
                throw NotFound();
            }

            return MapResponse(existing, language);
        }

        var draft = await _draftRepository.GetOwnedByOrderGuidAsync(
            orderGuid,
            deviceId,
            cancellationToken);
        if (draft is null)
        {
            throw NotFound();
        }

        if (draft.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            throw new BookingConfirmationException(
                BookingConfirmationProblemCodes.DraftExpired,
                StatusCodes.Status410Gone,
                "The checkout draft has expired.");
        }

        if (draft.PublicVersion != request.ExpectedVersion)
        {
            throw new BookingConfirmationException(
                BookingConfirmationProblemCodes.VersionConflict,
                StatusCodes.Status409Conflict,
                "The checkout draft version changed.");
        }

        if (draft.RequiresReprice)
        {
            throw new BookingConfirmationException(
                BookingConfirmationProblemCodes.DraftRequiresReprice,
                StatusCodes.Status409Conflict,
                "The checkout draft requires authoritative repricing.");
        }

        var pricing = draft.PricingSnapshot;
        if (pricing is null || pricing.Items.Count == 0)
        {
            throw new BookingConfirmationException(
                BookingConfirmationProblemCodes.DraftUnpriced,
                StatusCodes.Status409Conflict,
                "The checkout draft does not have authoritative pricing.");
        }

        ValidateSnapshot(pricing, draft);

        var user = await _context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == userId && value.IsActive, cancellationToken);
        if (user is null)
        {
            throw new BookingConfirmationException(
                BookingConfirmationProblemCodes.Invalid,
                StatusCodes.Status403Forbidden,
                "The authenticated customer is unavailable.");
        }

        var provider = await _catalogRepository.GetEnabledProviderBySourceCompanyIdWithGraphAsync(
            draft.BusinessSourceId,
            cancellationToken);
        var branch = provider?.Branches.SingleOrDefault(
            value => value.SourceBranchId == draft.BranchSourceId);
        if (provider is null || branch is null || provider.CatalogVersion != pricing.CatalogVersion)
        {
            throw new BookingConfirmationException(
                BookingConfirmationProblemCodes.DraftRequiresReprice,
                StatusCodes.Status409Conflict,
                "The catalog snapshot changed after pricing.");
        }

        var reservationRequest = await GetOrCreateAttemptRequestAsync(
            draft,
            pricing,
            user,
            deviceId,
            userId,
            request.CancellationPolicyAcknowledged,
            cancellationToken);
        var bookingReference = reservationRequest.BookingReference;
        CreateReservationResponse reservation;
        try
        {
            reservation = await _businessApiClient.CreateReservationAsync(
                reservationRequest,
                $"booking-{orderGuid:N}",
                cancellationToken);
        }
        catch (BusinessApiConflictException exception)
        {
            await ReleaseClaimAfterRejectionAsync(
                draft,
                invalidatePricing: exception.Code is ReservationErrorCodes.CatalogChanged
                    or ReservationErrorCodes.PriceChanged,
                cancellationToken);
            throw exception.Code switch
            {
                ReservationErrorCodes.CatalogChanged or ReservationErrorCodes.PriceChanged =>
                    new BookingConfirmationException(
                        BookingConfirmationProblemCodes.DraftRequiresReprice,
                        StatusCodes.Status409Conflict,
                        exception.Message,
                        exception.Code),
                ReservationErrorCodes.SlotUnavailable =>
                    new BookingConfirmationException(
                        BookingConfirmationProblemCodes.ReservationRejected,
                        StatusCodes.Status409Conflict,
                        exception.Message,
                        exception.Code),
                _ => new BookingConfirmationException(
                    BookingConfirmationProblemCodes.Unavailable,
                    StatusCodes.Status503ServiceUnavailable,
                    "Business rejected the internal reservation contract.",
                    exception.Code)
            };
        }
        catch (BusinessApiAuthenticationException exception)
        {
            throw UpstreamUnavailable(exception.Message);
        }
        catch (BusinessApiConfigurationException exception)
        {
            throw UpstreamUnavailable(exception.Message);
        }
        catch (BusinessApiTimeoutException exception)
        {
            throw UpstreamUnavailable(exception.Message);
        }
        catch (BusinessApiUnavailableException exception)
        {
            throw UpstreamUnavailable(exception.Message);
        }
        catch (BusinessApiContractException exception)
        {
            throw UpstreamUnavailable(exception.Message);
        }

        ValidateReservationResponse(reservation, bookingReference, draft, pricing);
        var booking = CreateBooking(
            draft,
            pricing,
            provider,
            branch,
            reservation,
            userId,
            deviceId,
            _timeProvider.GetUtcNow());

        _context.CustomerBookings.Add(booking);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.Entry(booking).State = EntityState.Detached;
            var replay = await LoadBookingAsync(orderGuid, cancellationToken);
            if (replay is null)
            {
                throw UpstreamUnavailable(
                    "The reservation was accepted but Customer persistence did not complete. Retry with the same request.");
            }

            if (replay.UserId != userId || replay.OwnerDeviceId != deviceId)
            {
                throw NotFound();
            }

            return MapResponse(replay, language);
        }

        _logger.LogInfo(
            $"Confirmed booking {booking.PublicReference:D} from checkout draft {orderGuid:D}.");
        return MapResponse(booking, language);
    }

    private Task<CustomerBooking?> LoadBookingAsync(
        Guid orderGuid,
        CancellationToken cancellationToken) =>
        _context.CustomerBookings
            .AsNoTracking()
            .Include(booking => booking.Items)
                .ThenInclude(item => item.Selections)
            .SingleOrDefaultAsync(booking => booking.OrderGuid == orderGuid, cancellationToken);

    private async Task<CreateReservationRequest> GetOrCreateAttemptRequestAsync(
        CheckoutDraft draft,
        CheckoutDraftPricingSnapshot pricing,
        User user,
        Guid deviceId,
        Guid userId,
        bool policyAcknowledged,
        CancellationToken cancellationToken)
    {
        var existing = await _context.BookingConfirmationAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                attempt => attempt.OrderGuid == draft.OrderGuid,
                cancellationToken);
        if (existing is not null)
        {
            EnsureAttemptOwnership(existing, draft, deviceId, userId);
            if (_context.Database.IsRelational())
            {
                var persistenceState =
                    await _draftRepository.GetPersistenceStateForUpdateAsync(
                        draft.Id,
                        cancellationToken);
                EnsurePersistenceClaimMatches(persistenceState, existing);
            }
            else
            {
                EnsureDraftClaimMatches(draft, existing);
            }

            return DeserializeAttempt(existing);
        }

        var originalRowVersion = draft.RowVersion.ToArray();
        var reservationRequest = CreateReservationRequest(
            draft,
            pricing,
            user,
            draft.OrderGuid,
            policyAcknowledged);
        var now = _timeProvider.GetUtcNow();
        var attempt = new BookingConfirmationAttempt
        {
            Id = Guid.NewGuid(),
            OrderGuid = draft.OrderGuid,
            BookingReference = reservationRequest.BookingReference,
            UserId = userId,
            OwnerDeviceId = deviceId,
            DraftVersion = draft.PublicVersion,
            ReservationRequestJson = JsonSerializer.Serialize(reservationRequest, JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        try
        {
            if (!_context.Database.IsRelational())
            {
                ClaimDraft(draft, attempt);
                _context.BookingConfirmationAttempts.Add(attempt);
                await _context.SaveChangesAsync(cancellationToken);
                return reservationRequest;
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction =
                    await _context.Database.BeginTransactionAsync(cancellationToken);
                var persistenceState = await _draftRepository.GetPersistenceStateForUpdateAsync(
                    draft.Id,
                    cancellationToken);
                existing = await _context.BookingConfirmationAttempts
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        value => value.OrderGuid == draft.OrderGuid,
                        cancellationToken);
                if (existing is not null)
                {
                    EnsureAttemptOwnership(existing, draft, deviceId, userId);
                    EnsurePersistenceClaimMatches(persistenceState, existing);
                    await transaction.CommitAsync(cancellationToken);
                    return DeserializeAttempt(existing);
                }

                if (persistenceState is null ||
                    persistenceState.PublicVersion != draft.PublicVersion ||
                    !persistenceState.RowVersion.SequenceEqual(originalRowVersion) ||
                    persistenceState.RequiresReprice ||
                    persistenceState.PricingSnapshotId != pricing.Id ||
                    persistenceState.ConfirmationClaimedVersion.HasValue ||
                    persistenceState.ExpiresAt <= _timeProvider.GetUtcNow())
                {
                    throw new DbUpdateConcurrencyException();
                }

                ClaimDraft(draft, attempt);
                _context.BookingConfirmationAttempts.Add(attempt);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return reservationRequest;
            });
        }
        catch (DbUpdateException)
        {
            _context.Entry(attempt).State = EntityState.Detached;
            _context.Entry(draft).State = EntityState.Detached;
            existing = await WaitForConcurrentAttemptAsync(
                draft.OrderGuid,
                cancellationToken);
            if (existing is null)
            {
                throw new BookingConfirmationException(
                    BookingConfirmationProblemCodes.VersionConflict,
                    StatusCodes.Status409Conflict,
                    "The checkout draft changed while confirmation was being claimed.");
            }

            EnsureAttemptOwnership(existing, draft, deviceId, userId);
            var persistenceState =
                await _draftRepository.GetPersistenceStateForUpdateAsync(
                    draft.Id,
                    cancellationToken);
            EnsurePersistenceClaimMatches(persistenceState, existing);
            return DeserializeAttempt(existing);
        }
    }

    private async Task<BookingConfirmationAttempt?> WaitForConcurrentAttemptAsync(
        Guid orderGuid,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var existing = await _context.BookingConfirmationAttempts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.OrderGuid == orderGuid,
                    cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            if (attempt < 9)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(50),
                    cancellationToken);
            }
        }

        return null;
    }

    private void ClaimDraft(CheckoutDraft draft, BookingConfirmationAttempt attempt)
    {
        draft.ConfirmationClaimedAtUtc = _timeProvider.GetUtcNow();
        draft.ConfirmationClaimedVersion = draft.PublicVersion;
        draft.ConfirmationBookingReference = attempt.BookingReference;
    }

    private static void EnsureDraftClaimMatches(
        CheckoutDraft draft,
        BookingConfirmationAttempt attempt)
    {
        if (draft.ConfirmationClaimedVersion != attempt.DraftVersion ||
            draft.ConfirmationBookingReference != attempt.BookingReference)
        {
            throw new BookingConfirmationException(
                BookingConfirmationProblemCodes.VersionConflict,
                StatusCodes.Status409Conflict,
                "The booking confirmation claim is inconsistent.");
        }
    }

    private static void EnsurePersistenceClaimMatches(
        CheckoutDraftPersistenceState? state,
        BookingConfirmationAttempt attempt)
    {
        if (state is null ||
            state.ConfirmationClaimedVersion != attempt.DraftVersion ||
            state.ConfirmationBookingReference != attempt.BookingReference)
        {
            throw new BookingConfirmationException(
                BookingConfirmationProblemCodes.VersionConflict,
                StatusCodes.Status409Conflict,
                "The booking confirmation claim is inconsistent.");
        }
    }

    private async Task ReleaseClaimAfterRejectionAsync(
        CheckoutDraft draft,
        bool invalidatePricing,
        CancellationToken cancellationToken)
    {
        var attempt = await _context.BookingConfirmationAttempts
            .SingleOrDefaultAsync(value => value.OrderGuid == draft.OrderGuid, cancellationToken);
        if (attempt is not null)
        {
            _context.BookingConfirmationAttempts.Remove(attempt);
        }

        draft.ConfirmationClaimedAtUtc = null;
        draft.ConfirmationClaimedVersion = null;
        draft.ConfirmationBookingReference = null;
        if (invalidatePricing)
        {
            draft.RequiresReprice = true;
            draft.PublicVersion = checked(draft.PublicVersion + 1);
            if (draft.PricingSnapshot is not null)
            {
                if (_context.Entry(draft.PricingSnapshot).State != EntityState.Detached)
                {
                    _context.CheckoutDraftPricingSnapshots.Remove(draft.PricingSnapshot);
                }

                draft.PricingSnapshot = null;
            }
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw UpstreamUnavailable(
                $"The rejected reservation state could not be safely persisted: {exception.GetType().Name}.");
        }
    }

    private static void EnsureAttemptOwnership(
        BookingConfirmationAttempt attempt,
        CheckoutDraft draft,
        Guid deviceId,
        Guid userId)
    {
        if (attempt.OwnerDeviceId != deviceId ||
            attempt.UserId != userId ||
            attempt.DraftVersion != draft.PublicVersion)
        {
            throw new BookingConfirmationException(
                BookingConfirmationProblemCodes.VersionConflict,
                StatusCodes.Status409Conflict,
                "The booking confirmation attempt conflicts with the current request.");
        }
    }

    private static CreateReservationRequest DeserializeAttempt(
        BookingConfirmationAttempt attempt)
    {
        try
        {
            return JsonSerializer.Deserialize<CreateReservationRequest>(
                    attempt.ReservationRequestJson,
                    JsonOptions)
                ?? throw new JsonException("The stored reservation request was null.");
        }
        catch (JsonException)
        {
            throw UpstreamUnavailable("The booking confirmation attempt could not be recovered.");
        }
    }

    private static void ValidateSnapshot(
        CheckoutDraftPricingSnapshot pricing,
        CheckoutDraft draft)
    {
        try
        {
            var baseSubtotal = pricing.Items.Aggregate(
                0m,
                (total, item) => checked(total + item.BaseSubtotal));
            var addonSubtotal = pricing.Items.Aggregate(
                0m,
                (total, item) => checked(total + item.AddonSubtotal));
            var itemSubtotal = pricing.Items.Aggregate(
                0m,
                (total, item) => checked(total + item.ItemSubtotal));
            var duration = pricing.Items.Aggregate(
                0,
                (total, item) => checked(total + item.TotalDurationMinutes));
            var grandTotal = checked(pricing.ItemSubtotal + pricing.ServiceFee + pricing.Tax);

            var invalid = pricing.CatalogVersion != draft.CatalogVersion ||
                string.IsNullOrWhiteSpace(pricing.Currency) ||
                baseSubtotal != pricing.BaseSubtotal ||
                addonSubtotal != pricing.AddonSubtotal ||
                itemSubtotal != pricing.ItemSubtotal ||
                duration != pricing.TotalDurationMinutes ||
                pricing.Items.Any(item =>
                    checked(item.BaseSubtotal + item.AddonSubtotal) != item.ItemSubtotal) ||
                grandTotal != pricing.GrandTotal ||
                pricing.TaxableSubtotal !=
                    (pricing.TaxAppliesToServiceFee
                        ? checked(pricing.ItemSubtotal + pricing.ServiceFee)
                        : pricing.ItemSubtotal);
            if (invalid)
            {
                throw new BookingConfirmationException(
                    BookingConfirmationProblemCodes.DraftUnpriced,
                    StatusCodes.Status409Conflict,
                    "The authoritative pricing snapshot is inconsistent.");
            }
        }
        catch (OverflowException)
        {
            throw new BookingConfirmationException(
                BookingConfirmationProblemCodes.DraftUnpriced,
                StatusCodes.Status409Conflict,
                "The authoritative pricing snapshot exceeds supported limits.");
        }
    }

    private static CreateReservationRequest CreateReservationRequest(
        CheckoutDraft draft,
        CheckoutDraftPricingSnapshot pricing,
        User user,
        Guid bookingReference,
        bool policyAcknowledged) =>
        new()
        {
            BookingReference = bookingReference,
            OrderGuid = draft.OrderGuid,
            BranchId = draft.BranchSourceId,
            ExpectedCatalogVersion = pricing.CatalogVersion,
            RequestedSlotStartUtc = draft.RequestedSlotStartUtc,
            Currency = pricing.Currency,
            ExpectedItemSubtotal = pricing.ItemSubtotal,
            ExpectedTotalDurationMinutes = pricing.TotalDurationMinutes,
            Customer = new ReservationCustomerSnapshot
            {
                Name = user.FullName,
                Email = user.Email,
                Phone = user.Phone
            },
            Vehicle = new ReservationVehicleSnapshot
            {
                VehicleType = draft.VehicleType,
                LicensePlate = draft.LicensePlate,
                Make = draft.VehicleMake,
                Model = draft.VehicleModel,
                Color = draft.VehicleColor
            },
            Location = new ReservationLocationSnapshot
            {
                AddressLine = draft.AddressLine,
                City = draft.City,
                Area = draft.Area,
                Latitude = Convert.ToDouble(draft.Latitude),
                Longitude = Convert.ToDouble(draft.Longitude)
            },
            CancellationPolicyAcknowledged = policyAcknowledged,
            Items = pricing.Items
                .OrderBy(item => item.DisplayOrder)
                .Select(item => new CreateReservationItemRequest
                {
                    OfferingId = item.OfferingSourceId,
                    ExpectedBaseSubtotal = item.BaseSubtotal,
                    ExpectedAddonSubtotal = item.AddonSubtotal,
                    ExpectedItemSubtotal = item.ItemSubtotal,
                    ExpectedDurationMinutes = item.TotalDurationMinutes,
                    SelectedAddons = item.Selections
                        .Where(selection => !selection.IsDefaultApplied)
                        .OrderBy(selection => selection.DisplayOrder)
                        .Select(selection => new ValidateAppointmentAddonSelectionRequest
                        {
                            AddonChoiceId = selection.AddonChoiceSourceId,
                            Quantity = selection.Quantity
                        }).ToArray()
                }).ToArray()
        };

    private static void ValidateReservationResponse(
        CreateReservationResponse response,
        Guid bookingReference,
        CheckoutDraft draft,
        CheckoutDraftPricingSnapshot pricing)
    {
        var expectedItems = pricing.Items.ToArray();
        var actualItems = response.Items?.ToArray() ?? [];
        var valid = response.ContractVersion ==
                Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract.Version &&
            response.BookingReference == bookingReference &&
            response.ReservationId != Guid.Empty &&
            response.WorkOrderId != Guid.Empty &&
            response.Status == ReservationStatuses.Pending &&
            response.CatalogVersion == pricing.CatalogVersion &&
            string.Equals(response.Currency, pricing.Currency, StringComparison.Ordinal) &&
            response.ItemSubtotal == pricing.ItemSubtotal &&
            response.TotalDurationMinutes == pricing.TotalDurationMinutes &&
            response.RequestedSlotStartUtc.Offset == TimeSpan.Zero &&
            response.RequestedSlotEndUtc.Offset == TimeSpan.Zero &&
            response.RequestedSlotStartUtc == draft.RequestedSlotStartUtc &&
            response.RequestedSlotEndUtc ==
                draft.RequestedSlotStartUtc.AddMinutes(pricing.TotalDurationMinutes) &&
            (response.Errors?.Count ?? 0) == 0 &&
            actualItems.Length == expectedItems.Length;

        var expectedItemsByOffering = new Dictionary<Guid, CheckoutDraftPricingItemSnapshot>();
        foreach (var expectedItem in expectedItems)
        {
            valid &= expectedItemsByOffering.TryAdd(expectedItem.OfferingSourceId, expectedItem);
        }

        var actualItemsByOffering = new Dictionary<Guid, ReservationAcceptedItem>();
        foreach (var actualItem in actualItems)
        {
            valid &= actualItemsByOffering.TryAdd(actualItem.OfferingId, actualItem);
        }

        foreach (var expected in expectedItems)
        {
            if (!valid ||
                !actualItemsByOffering.TryGetValue(expected.OfferingSourceId, out var actual))
            {
                valid = false;
                break;
            }

            var expectedSelections = expected.Selections.ToArray();
            var actualSelections = actual.Selections?.ToArray() ?? [];
            valid = actual.BaseSubtotal == expected.BaseSubtotal &&
                actual.AddonSubtotal == expected.AddonSubtotal &&
                actual.ItemSubtotal == expected.ItemSubtotal &&
                actual.TotalDurationMinutes == expected.TotalDurationMinutes &&
                actualSelections.Length == expectedSelections.Length;

            var expectedSelectionsByAddon = new Dictionary<
                Guid,
                CheckoutDraftPricingSelectionSnapshot>();
            foreach (var expectedSelection in expectedSelections)
            {
                valid &= expectedSelectionsByAddon.TryAdd(
                    expectedSelection.AddonChoiceSourceId,
                    expectedSelection);
            }

            var actualSelectionsByAddon = new Dictionary<Guid, NormalizedAddonSelection>();
            foreach (var actualSelection in actualSelections)
            {
                valid &= actualSelectionsByAddon.TryAdd(
                    actualSelection.AddonChoiceId,
                    actualSelection);
            }

            foreach (var expectedSelection in expectedSelections)
            {
                if (!valid ||
                    !actualSelectionsByAddon.TryGetValue(
                        expectedSelection.AddonChoiceSourceId,
                        out var actualSelection))
                {
                    valid = false;
                    break;
                }

                valid = actualSelection.AddonGroupId == expectedSelection.AddonGroupSourceId &&
                    actualSelection.AddonChoiceId == expectedSelection.AddonChoiceSourceId &&
                    actualSelection.SelectionType == expectedSelection.SelectionType &&
                    actualSelection.Quantity == expectedSelection.Quantity &&
                    actualSelection.UnitPriceAdjustment == expectedSelection.UnitPriceAdjustment &&
                    actualSelection.TotalPriceAdjustment == expectedSelection.TotalPriceAdjustment &&
                    actualSelection.UnitDurationAdjustmentMinutes ==
                        expectedSelection.UnitDurationAdjustmentMinutes &&
                    actualSelection.TotalDurationAdjustmentMinutes ==
                        expectedSelection.TotalDurationAdjustmentMinutes &&
                    actualSelection.IsDefaultApplied == expectedSelection.IsDefaultApplied;
            }
        }

        if (!valid)
        {
            throw UpstreamUnavailable("The reservation response did not match the confirmed draft.");
        }
    }

    private static CustomerBooking CreateBooking(
        CheckoutDraft draft,
        CheckoutDraftPricingSnapshot pricing,
        CatalogProviderReadModel provider,
        CatalogBranchReadModel branch,
        CreateReservationResponse reservation,
        Guid userId,
        Guid deviceId,
        DateTimeOffset now)
    {
        var offerings = provider.Categories
            .SelectMany(category => category.Offerings)
            .ToDictionary(offering => offering.SourceOfferingId);

        return new CustomerBooking
        {
            Id = Guid.NewGuid(),
            PublicReference = reservation.BookingReference,
            OrderGuid = draft.OrderGuid,
            UserId = userId,
            OwnerDeviceId = deviceId,
            BusinessReservationId = reservation.ReservationId,
            BusinessWorkOrderId = reservation.WorkOrderId,
            BusinessSourceId = draft.BusinessSourceId,
            BranchSourceId = draft.BranchSourceId,
            CatalogVersion = pricing.CatalogVersion,
            ConfirmedDraftVersion = draft.PublicVersion,
            Status = reservation.Status,
            BusinessStatusSequence = 0,
            StatusChangedAtUtc = now,
            RequestedSlotStartUtc = reservation.RequestedSlotStartUtc,
            RequestedSlotEndUtc = reservation.RequestedSlotEndUtc,
            ProviderNameAr = provider.NameAr,
            ProviderNameHe = provider.NameHe,
            BranchNameAr = branch.NameAr,
            BranchNameHe = branch.NameHe,
            VehicleType = draft.VehicleType,
            LicensePlate = draft.LicensePlate,
            VehicleMake = draft.VehicleMake,
            VehicleModel = draft.VehicleModel,
            VehicleColor = draft.VehicleColor,
            AddressLine = draft.AddressLine,
            City = draft.City,
            Area = draft.Area,
            Latitude = draft.Latitude,
            Longitude = draft.Longitude,
            Currency = pricing.Currency,
            BaseSubtotal = pricing.BaseSubtotal,
            AddonSubtotal = pricing.AddonSubtotal,
            ItemSubtotal = pricing.ItemSubtotal,
            ServiceFee = pricing.ServiceFee,
            ServiceFeeMode = pricing.ServiceFeeMode,
            ServiceFeeFlatAmount = pricing.ServiceFeeFlatAmount,
            ServiceFeePercentageRate = pricing.ServiceFeePercentageRate,
            TaxableSubtotal = pricing.TaxableSubtotal,
            TaxRatePercent = pricing.TaxRatePercent,
            TaxAppliesToServiceFee = pricing.TaxAppliesToServiceFee,
            Tax = pricing.Tax,
            GrandTotal = pricing.GrandTotal,
            TotalDurationMinutes = pricing.TotalDurationMinutes,
            QuotedAtUtc = pricing.QuotedAtUtc,
            CreatedAtUtc = now,
            Items = pricing.Items.OrderBy(item => item.DisplayOrder).Select(item =>
            {
                if (!offerings.TryGetValue(item.OfferingSourceId, out var offering))
                {
                    throw new BookingConfirmationException(
                        BookingConfirmationProblemCodes.DraftRequiresReprice,
                        StatusCodes.Status409Conflict,
                        "A priced service is no longer present in the catalog.");
                }

                var groups = offering.AddonGroups.ToDictionary(group => group.SourceAddonGroupId);
                var choices = offering.AddonGroups
                    .SelectMany(group => group.Choices.Select(choice => (group, choice)))
                    .ToDictionary(value => value.choice.SourceAddonChoiceId);
                return new CustomerBookingItem
                {
                    Id = Guid.NewGuid(),
                    OfferingSourceId = item.OfferingSourceId,
                    ServiceNameAr = offering.NameAr,
                    ServiceNameHe = offering.NameHe,
                    DisplayOrder = item.DisplayOrder,
                    BaseSubtotal = item.BaseSubtotal,
                    AddonSubtotal = item.AddonSubtotal,
                    ItemSubtotal = item.ItemSubtotal,
                    TotalDurationMinutes = item.TotalDurationMinutes,
                    Selections = item.Selections.OrderBy(selection => selection.DisplayOrder).Select(selection =>
                    {
                        if (!groups.TryGetValue(selection.AddonGroupSourceId, out var group) ||
                            !choices.TryGetValue(selection.AddonChoiceSourceId, out var choice))
                        {
                            throw new BookingConfirmationException(
                                BookingConfirmationProblemCodes.DraftRequiresReprice,
                                StatusCodes.Status409Conflict,
                                "A priced add-on is no longer present in the catalog.");
                        }

                        return new CustomerBookingSelection
                        {
                            Id = Guid.NewGuid(),
                            AddonGroupSourceId = selection.AddonGroupSourceId,
                            AddonChoiceSourceId = selection.AddonChoiceSourceId,
                            AddonGroupNameAr = group.NameAr,
                            AddonGroupNameHe = group.NameHe,
                            AddonChoiceNameAr = choice.choice.NameAr,
                            AddonChoiceNameHe = choice.choice.NameHe,
                            SelectionType = selection.SelectionType,
                            Quantity = selection.Quantity,
                            UnitPriceAdjustment = selection.UnitPriceAdjustment,
                            TotalPriceAdjustment = selection.TotalPriceAdjustment,
                            UnitDurationAdjustmentMinutes = selection.UnitDurationAdjustmentMinutes,
                            TotalDurationAdjustmentMinutes = selection.TotalDurationAdjustmentMinutes,
                            IsDefaultApplied = selection.IsDefaultApplied,
                            DisplayOrder = selection.DisplayOrder
                        };
                    }).ToArray()
                };
            }).ToArray()
        };
    }

    private static ConfirmedBookingResponse MapResponse(
        CustomerBooking booking,
        string language)
    {
        var hebrew = language == ConfigurationLanguageResolver.Hebrew;
        return new ConfirmedBookingResponse
        {
            Id = booking.Id,
            Reference = booking.PublicReference,
            OrderGuid = booking.OrderGuid,
            BusinessReservationId = booking.BusinessReservationId,
            BusinessWorkOrderId = booking.BusinessWorkOrderId,
            Status = booking.Status,
            DraftVersion = booking.ConfirmedDraftVersion,
            Language = language,
            RequestedSlotStartUtc = booking.RequestedSlotStartUtc,
            RequestedSlotEndUtc = booking.RequestedSlotEndUtc,
            ProviderName = hebrew && !string.IsNullOrWhiteSpace(booking.ProviderNameHe)
                ? booking.ProviderNameHe
                : booking.ProviderNameAr,
            BranchName = hebrew && !string.IsNullOrWhiteSpace(booking.BranchNameHe)
                ? booking.BranchNameHe
                : booking.BranchNameAr,
            Currency = booking.Currency,
            GrandTotal = booking.GrandTotal,
            TotalDurationMinutes = booking.TotalDurationMinutes,
            Items = booking.Items.OrderBy(item => item.DisplayOrder).Select(item =>
                new ConfirmedBookingItemResponse
                {
                    OfferingSourceId = item.OfferingSourceId,
                    ServiceName = hebrew && !string.IsNullOrWhiteSpace(item.ServiceNameHe)
                        ? item.ServiceNameHe
                        : item.ServiceNameAr,
                    ItemSubtotal = item.ItemSubtotal,
                    TotalDurationMinutes = item.TotalDurationMinutes
                }).ToArray()
        };
    }

    private static BookingConfirmationException NotFound() =>
        new(
            BookingConfirmationProblemCodes.DraftNotFound,
            StatusCodes.Status404NotFound,
            "The checkout draft was not found.");

    private static BookingConfirmationException UpstreamUnavailable(string message) =>
        new(
            BookingConfirmationProblemCodes.Unavailable,
            StatusCodes.Status503ServiceUnavailable,
            message);
}
