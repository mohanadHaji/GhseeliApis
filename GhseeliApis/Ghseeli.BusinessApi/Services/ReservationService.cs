using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.Bookings;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ghseeli.BusinessApi.Services;

public sealed class ReservationService : IReservationService
{
    private readonly BusinessDbContext _context;
    private readonly IAppointmentValidationService _validationService;
    private readonly ITimeZoneAvailabilityResolver _availabilityResolver;
    private readonly ISystemClock _clock;
    private readonly IAppLogger _logger;

    public ReservationService(
        BusinessDbContext context,
        IAppointmentValidationService validationService,
        ITimeZoneAvailabilityResolver availabilityResolver,
        ISystemClock clock,
        IAppLogger logger)
    {
        _context = context;
        _validationService = validationService;
        _availabilityResolver = availabilityResolver;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CreateReservationResponse> CreateAsync(
        CreateReservationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var existing = await LoadExistingAsync(request.OrderGuid, cancellationToken);
        if (existing is not null)
        {
            EnsureEquivalent(existing, request);
            return MapResponse(existing);
        }

        var strategy = _context.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var transaction = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken)
                    : null;

                var replay = await LoadExistingAsync(request.OrderGuid, cancellationToken);
                if (replay is not null)
                {
                    EnsureEquivalent(replay, request);
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                    }

                    return MapResponse(replay);
                }

                if (_context.Database.IsRelational())
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT [Id] FROM [Branches] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {request.BranchId}",
                        cancellationToken);
                }

                var acceptedItems = await ValidateAuthoritativelyAsync(request);
                var (itemSubtotal, totalDuration) = CalculateTotals(request, acceptedItems);
                var startUtc = request.RequestedSlotStartUtc.UtcDateTime;
                var endUtc = startUtc.AddMinutes(totalDuration);

                var branch = await _context.Branches
                    .Include(value => value.Company)
                    .Include(value => value.AvailabilitySettings)
                    .Include(value => value.RecurringSchedules)
                    .Include(value => value.AvailabilityOverrides)
                    .SingleOrDefaultAsync(value => value.Id == request.BranchId, cancellationToken);
                if (branch is null)
                {
                    throw new ReservationRejectedException(
                        ReservationErrorCodes.SlotUnavailable,
                        "Availability is not configured.");
                }

                var availability = _availabilityResolver.Resolve(
                    branch,
                    startUtc,
                    totalDuration);
                var capacity = availability.IsValid
                    ? availability.Facts.ConfiguredCapacity ?? 0
                    : 0;
                var occupied = await _context.AppointmentReservations.CountAsync(
                    reservation =>
                        reservation.BranchId == request.BranchId &&
                        BookingStatuses.CapacityOccupying.Contains(reservation.Status) &&
                        reservation.RequestedSlotStartUtc < endUtc &&
                        reservation.RequestedSlotEndUtc > startUtc,
                    cancellationToken);
                if (capacity <= 0 || occupied >= capacity)
                {
                    throw new ReservationRejectedException(
                        ReservationErrorCodes.SlotUnavailable,
                        availability.Error?.Message ??
                        "The requested slot has no remaining capacity.",
                        availability.Error is null ? null : [availability.Error]);
                }

                var now = _clock.UtcNow;
                var reservation = new AppointmentReservation
                {
                    Id = Guid.NewGuid(),
                    PublicId = Guid.NewGuid(),
                    CustomerBookingReference = request.BookingReference,
                    OrderGuid = request.OrderGuid,
                    RequestHash = ComputeRequestHash(request),
                    BranchId = request.BranchId,
                    BusinessVerticalId = BusinessVerticalDefaults.CarWashId,
                    BusinessVerticalCode = BusinessVerticalDefaults.CarWashCode,
                    CatalogVersion = request.ExpectedCatalogVersion,
                    Currency = request.Currency,
                    ItemSubtotal = itemSubtotal,
                    TotalDurationMinutes = totalDuration,
                    RequestedSlotStartUtc = startUtc,
                    RequestedSlotEndUtc = endUtc,
                    Status = ReservationStatuses.Pending,
                    StatusSequence = 0,
                    StatusChangedAtUtc = now,
                    CreatedAtUtc = now,
                    WorkOrder = new WorkOrder
                    {
                        Id = Guid.NewGuid(),
                        PublicId = Guid.NewGuid(),
                        Status = ReservationStatuses.Pending,
                        BusinessVerticalId = BusinessVerticalDefaults.CarWashId,
                        BusinessVerticalCode = BusinessVerticalDefaults.CarWashCode,
                        CustomerName = request.Customer.Name,
                        CustomerEmail = request.Customer.Email,
                        CustomerPhone = request.Customer.Phone,
                        VehicleType = request.Vehicle.VehicleType,
                        LicensePlate = request.Vehicle.LicensePlate,
                        VehicleMake = request.Vehicle.Make,
                        VehicleModel = request.Vehicle.Model,
                        VehicleColor = request.Vehicle.Color,
                        AddressLine = request.Location.AddressLine,
                        City = request.Location.City,
                        Area = request.Location.Area,
                        Latitude = Convert.ToDecimal(request.Location.Latitude),
                        Longitude = Convert.ToDecimal(request.Location.Longitude),
                        CreatedAtUtc = now,
                        Items = acceptedItems.Select((item, itemIndex) => new WorkOrderItem
                        {
                            Id = Guid.NewGuid(),
                            OfferingId = item.OfferingId,
                            DisplayOrder = itemIndex,
                            BaseSubtotal = item.BaseSubtotal,
                            AddonSubtotal = item.AddonSubtotal,
                            ItemSubtotal = item.ItemSubtotal,
                            TotalDurationMinutes = item.TotalDurationMinutes,
                            Selections = item.Selections.Select((selection, selectionIndex) =>
                                new WorkOrderSelection
                                {
                                    Id = Guid.NewGuid(),
                                    AddonGroupId = selection.AddonGroupId,
                                    AddonChoiceId = selection.AddonChoiceId,
                                    SelectionType = selection.SelectionType,
                                    Quantity = selection.Quantity,
                                    UnitPriceAdjustment = selection.UnitPriceAdjustment,
                                    TotalPriceAdjustment = selection.TotalPriceAdjustment,
                                    UnitDurationAdjustmentMinutes = selection.UnitDurationAdjustmentMinutes,
                                    TotalDurationAdjustmentMinutes = selection.TotalDurationAdjustmentMinutes,
                                    IsDefaultApplied = selection.IsDefaultApplied,
                                    DisplayOrder = selectionIndex
                                }).ToArray()
                        }).ToArray()
                    }
                };

                _context.AppointmentReservations.Add(reservation);
                await _context.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                _logger.LogInfo(
                    $"Created reservation {reservation.PublicId:D} for booking reference {reservation.CustomerBookingReference:D}.");
                return MapResponse(reservation);
            });
        }
        catch (Exception exception) when (
            exception is not ReservationRejectedException &&
            exception is not OperationCanceledException)
        {
            _context.ChangeTracker.Clear();
            var replay = await LoadExistingAsync(request.OrderGuid, cancellationToken);
            if (replay is null)
            {
                throw;
            }

            EnsureEquivalent(replay, request);
            return MapResponse(replay);
        }
    }

    private async Task<IReadOnlyList<ReservationAcceptedItem>> ValidateAuthoritativelyAsync(
        CreateReservationRequest request)
    {
        var acceptedItems = new List<ReservationAcceptedItem>(request.Items.Count);
        foreach (var item in request.Items.OrderBy(value => value.OfferingId))
        {
            var validation = await _validationService.ValidateAsync(new ValidateAppointmentRequest
            {
                BranchId = request.BranchId,
                OfferingId = item.OfferingId,
                SelectedAddons = item.SelectedAddons
                    .OrderBy(value => value.AddonChoiceId)
                    .ThenBy(value => value.Quantity)
                    .ToArray(),
                RequestedSlotStartUtc = request.RequestedSlotStartUtc,
                CustomerLocation = new AppointmentCustomerLocationFacts
                {
                    Latitude = request.Location.Latitude,
                    Longitude = request.Location.Longitude
                },
                ExpectedCatalogVersion = request.ExpectedCatalogVersion,
                Currency = request.Currency
            });

            if (!validation.Valid ||
                validation.CatalogVersion != request.ExpectedCatalogVersion)
            {
                var code = validation.CatalogVersion != request.ExpectedCatalogVersion ||
                    validation.Errors.Any(error =>
                        error.Code == AppointmentValidationErrorCodes.StaleCatalogVersion)
                    ? ReservationErrorCodes.CatalogChanged
                    : validation.Errors.Any(error =>
                        error.Code is AppointmentValidationErrorCodes.SlotUnavailable
                            or AppointmentValidationErrorCodes.SlotMisaligned
                            or AppointmentValidationErrorCodes.SlotBeforeLeadTime
                            or AppointmentValidationErrorCodes.SlotBeyondHorizon)
                        ? ReservationErrorCodes.SlotUnavailable
                        : ReservationErrorCodes.Invalid;
                throw new ReservationRejectedException(
                    code,
                    "The reservation was rejected by authoritative validation.",
                    validation.Errors);
            }

            if (validation.BaseSubtotal != item.ExpectedBaseSubtotal ||
                validation.AddonSubtotal != item.ExpectedAddonSubtotal ||
                validation.TotalPrice != item.ExpectedItemSubtotal ||
                validation.TotalDurationMinutes != item.ExpectedDurationMinutes)
            {
                throw new ReservationRejectedException(
                    ReservationErrorCodes.PriceChanged,
                    "The authoritative price or duration changed.");
            }

            acceptedItems.Add(new ReservationAcceptedItem
            {
                OfferingId = validation.OfferingId,
                BaseSubtotal = validation.BaseSubtotal,
                AddonSubtotal = validation.AddonSubtotal,
                ItemSubtotal = validation.TotalPrice,
                TotalDurationMinutes = validation.TotalDurationMinutes,
                Selections = validation.NormalizedSelections
                    .OrderBy(value => value.AddonGroupId)
                    .ThenBy(value => value.AddonChoiceId)
                    .ThenBy(value => value.Quantity)
                    .ToArray()
            });
        }

        return acceptedItems;
    }

    private static (decimal ItemSubtotal, int TotalDuration) CalculateTotals(
        CreateReservationRequest request,
        IReadOnlyCollection<ReservationAcceptedItem> acceptedItems)
    {
        decimal itemSubtotal;
        int totalDuration;
        try
        {
            itemSubtotal = acceptedItems.Aggregate(
                0m,
                (total, item) => checked(total + item.ItemSubtotal));
            totalDuration = acceptedItems.Aggregate(
                0,
                (total, item) => checked(total + item.TotalDurationMinutes));
        }
        catch (OverflowException)
        {
            throw new ReservationRejectedException(
                ReservationErrorCodes.Invalid,
                "The reservation totals exceed supported limits.");
        }

        if (itemSubtotal != request.ExpectedItemSubtotal ||
            totalDuration != request.ExpectedTotalDurationMinutes)
        {
            throw new ReservationRejectedException(
                ReservationErrorCodes.PriceChanged,
                "The authoritative reservation totals changed.");
        }

        return (itemSubtotal, totalDuration);
    }

    private Task<AppointmentReservation?> LoadExistingAsync(
        Guid orderGuid,
        CancellationToken cancellationToken) =>
        _context.AppointmentReservations
            .Include(reservation => reservation.WorkOrder)
                .ThenInclude(workOrder => workOrder.Items)
                    .ThenInclude(item => item.Selections)
            .SingleOrDefaultAsync(
                reservation => reservation.OrderGuid == orderGuid,
                cancellationToken);

    private static void ValidateRequest(CreateReservationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Items is null ||
            request.Items.Count == 0 ||
            request.Items.Count > 25 ||
            request.Items.Any(item =>
                item is null ||
                item.SelectedAddons is null ||
                item.SelectedAddons.Any(selection => selection is null)))
        {
            throw new ReservationRejectedException(
                ReservationErrorCodes.Invalid,
                "The reservation request is invalid.");
        }

        if (request.BookingReference == Guid.Empty ||
            request.OrderGuid == Guid.Empty ||
            request.BranchId == Guid.Empty ||
            request.ExpectedCatalogVersion <= 0 ||
            request.RequestedSlotStartUtc == default ||
            string.IsNullOrWhiteSpace(request.Currency) ||
            request.Items.Any(item =>
                item.OfferingId == Guid.Empty ||
                item.ExpectedBaseSubtotal < 0 ||
                item.ExpectedAddonSubtotal < 0 ||
                item.ExpectedItemSubtotal < 0 ||
                item.ExpectedDurationMinutes <= 0) ||
            request.Items.Select(item => item.OfferingId).Distinct().Count() != request.Items.Count ||
            request.ExpectedItemSubtotal < 0 ||
            request.ExpectedTotalDurationMinutes <= 0 ||
            request.Customer is null ||
            string.IsNullOrWhiteSpace(request.Customer.Name) ||
            request.Customer.Name.Length > 150 ||
            request.Customer.Email?.Length > 254 ||
            request.Customer.Phone?.Length > 32 ||
            request.Vehicle is null ||
            string.IsNullOrWhiteSpace(request.Vehicle.VehicleType) ||
            request.Vehicle.VehicleType.Length > 50 ||
            request.Location is null ||
            string.IsNullOrWhiteSpace(request.Location.AddressLine) ||
            request.Location.AddressLine.Length > 300 ||
            !double.IsFinite(request.Location.Latitude) ||
            !double.IsFinite(request.Location.Longitude) ||
            request.Location.Latitude is < -90 or > 90 ||
            request.Location.Longitude is < -180 or > 180 ||
            !request.CancellationPolicyAcknowledged)
        {
            throw new ReservationRejectedException(
                ReservationErrorCodes.Invalid,
                "The reservation request is invalid.");
        }

    }

    private static void EnsureEquivalent(
        AppointmentReservation reservation,
        CreateReservationRequest request)
    {
        if (!string.Equals(
                reservation.RequestHash,
                ComputeRequestHash(request),
                StringComparison.Ordinal))
        {
            throw new ReservationRejectedException(
                ReservationErrorCodes.Invalid,
                "The order identifier is already associated with another reservation request.",
                isStateConflict: true);
        }

    }

    private static string ComputeRequestHash(CreateReservationRequest request)
    {
        var canonical = new StringBuilder();
        Append(canonical, Normalize(request.ContractVersion));
        Append(canonical, request.BookingReference);
        Append(canonical, request.OrderGuid);
        Append(canonical, request.BranchId);
        Append(canonical, request.ExpectedCatalogVersion);
        Append(canonical, request.RequestedSlotStartUtc.UtcTicks);
        Append(canonical, Normalize(request.Currency).ToUpperInvariant());
        Append(canonical, request.ExpectedItemSubtotal);
        Append(canonical, request.ExpectedTotalDurationMinutes);
        Append(canonical, Normalize(request.Customer.Name));
        Append(canonical, NormalizeNullable(request.Customer.Email));
        Append(canonical, NormalizeNullable(request.Customer.Phone));
        Append(canonical, Normalize(request.Vehicle.VehicleType));
        Append(canonical, NormalizeNullable(request.Vehicle.LicensePlate));
        Append(canonical, NormalizeNullable(request.Vehicle.Make));
        Append(canonical, NormalizeNullable(request.Vehicle.Model));
        Append(canonical, NormalizeNullable(request.Vehicle.Color));
        Append(canonical, Normalize(request.Location.AddressLine));
        Append(canonical, NormalizeNullable(request.Location.City));
        Append(canonical, NormalizeNullable(request.Location.Area));
        Append(canonical, NormalizeDouble(request.Location.Latitude));
        Append(canonical, NormalizeDouble(request.Location.Longitude));
        Append(canonical, request.CancellationPolicyAcknowledged);

        foreach (var item in request.Items.OrderBy(value => value.OfferingId))
        {
            Append(canonical, item.OfferingId);
            Append(canonical, item.ExpectedBaseSubtotal);
            Append(canonical, item.ExpectedAddonSubtotal);
            Append(canonical, item.ExpectedItemSubtotal);
            Append(canonical, item.ExpectedDurationMinutes);
            foreach (var selection in item.SelectedAddons
                         .OrderBy(value => value.AddonChoiceId)
                         .ThenBy(value => value.Quantity))
            {
                Append(canonical, selection.AddonChoiceId);
                Append(canonical, selection.Quantity);
            }
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static string Normalize(string value) => value.Trim();

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeDouble(double value) =>
        value == 0d ? "0" : value.ToString("R", CultureInfo.InvariantCulture);

    private static void Append(StringBuilder builder, object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            decimal decimalValue => decimalValue.ToString("G29", CultureInfo.InvariantCulture),
            bool boolValue => boolValue ? "1" : "0",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
        builder.Append(text.Length).Append(':').Append(text).Append('|');
    }

    private static CreateReservationResponse MapResponse(AppointmentReservation reservation) =>
        new()
        {
            BookingReference = reservation.CustomerBookingReference,
            ReservationId = reservation.PublicId,
            WorkOrderId = reservation.WorkOrder.PublicId,
            Status = reservation.Status,
            CatalogVersion = reservation.CatalogVersion,
            Currency = reservation.Currency,
            ItemSubtotal = reservation.ItemSubtotal,
            TotalDurationMinutes = reservation.TotalDurationMinutes,
            RequestedSlotStartUtc = new DateTimeOffset(
                DateTime.SpecifyKind(reservation.RequestedSlotStartUtc, DateTimeKind.Utc)),
            RequestedSlotEndUtc = new DateTimeOffset(
                DateTime.SpecifyKind(reservation.RequestedSlotEndUtc, DateTimeKind.Utc)),
            ReservationExpiresAtUtc = reservation.ExpiresAtUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(reservation.ExpiresAtUtc.Value, DateTimeKind.Utc))
                : null,
            Items = reservation.WorkOrder.Items
                .OrderBy(item => item.DisplayOrder)
                .Select(item => new ReservationAcceptedItem
                {
                    OfferingId = item.OfferingId,
                    BaseSubtotal = item.BaseSubtotal,
                    AddonSubtotal = item.AddonSubtotal,
                    ItemSubtotal = item.ItemSubtotal,
                    TotalDurationMinutes = item.TotalDurationMinutes,
                    Selections = item.Selections
                        .OrderBy(selection => selection.DisplayOrder)
                        .Select(selection => new NormalizedAddonSelection
                        {
                            AddonGroupId = selection.AddonGroupId,
                            AddonChoiceId = selection.AddonChoiceId,
                            SelectionType = selection.SelectionType,
                            Quantity = selection.Quantity,
                            UnitPriceAdjustment = selection.UnitPriceAdjustment,
                            TotalPriceAdjustment = selection.TotalPriceAdjustment,
                            UnitDurationAdjustmentMinutes = selection.UnitDurationAdjustmentMinutes,
                            TotalDurationAdjustmentMinutes = selection.TotalDurationAdjustmentMinutes,
                            IsDefaultApplied = selection.IsDefaultApplied
                        }).ToArray()
                }).ToArray()
        };
}
