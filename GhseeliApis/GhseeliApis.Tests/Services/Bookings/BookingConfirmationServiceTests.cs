using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.DTOs.Booking;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Bookings;
using GhseeliApis.Services.Business;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GhseeliApis.Tests.Services.Bookings;

/// <summary>
/// Verifies authoritative, owned and idempotent checkout confirmation.
/// </summary>
public class BookingConfirmationServiceTests
{
    [Fact]
    public async Task ConfirmAsync_WithPricedOwnedDraft_PersistsImmutableSnapshotsAndReplays()
    {
        var fixture = await CreateFixtureAsync();

        var first = await fixture.Service.ConfirmAsync(
            fixture.Draft.OrderGuid,
            ValidRequest(fixture.Draft.PublicVersion),
            fixture.DeviceId,
            fixture.UserId,
            "he",
            null,
            CancellationToken.None);
        var second = await fixture.Service.ConfirmAsync(
            fixture.Draft.OrderGuid,
            ValidRequest(fixture.Draft.PublicVersion),
            fixture.DeviceId,
            fixture.UserId,
            "ar",
            null,
            CancellationToken.None);

        first.Reference.Should().Be(second.Reference);
        first.BusinessReservationId.Should().Be(second.BusinessReservationId);
        first.ProviderName.Should().Be("Provider HE");
        second.ProviderName.Should().Be("Provider AR");
        fixture.BusinessClient.CreateReservationRequests.Should().Be(1);

        var stored = await fixture.Context.CustomerBookings
            .Include(booking => booking.Items)
                .ThenInclude(item => item.Selections)
            .SingleAsync();
        stored.OrderGuid.Should().Be(fixture.Draft.OrderGuid);
        stored.UserId.Should().Be(fixture.UserId);
        stored.OwnerDeviceId.Should().Be(fixture.DeviceId);
        stored.GrandTotal.Should().Be(126m);
        stored.Items.Should().ContainSingle();
        stored.Items.Single().ServiceNameAr.Should().Be("Service AR");
        stored.Items.Single().Selections.Should().ContainSingle();
    }

    [Fact]
    public async Task ConfirmAsync_FromDifferentDevice_DoesNotCallBusiness()
    {
        var fixture = await CreateFixtureAsync();

        var action = () => fixture.Service.ConfirmAsync(
            fixture.Draft.OrderGuid,
            ValidRequest(fixture.Draft.PublicVersion),
            Guid.NewGuid(),
            fixture.UserId,
            "ar",
            null,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingConfirmationException>();
        exception.Which.Code.Should().Be(BookingConfirmationProblemCodes.DraftNotFound);
        fixture.BusinessClient.CreateReservationRequests.Should().Be(0);
    }

    [Theory]
    [InlineData(true, true, 2, BookingConfirmationProblemCodes.DraftRequiresReprice)]
    [InlineData(false, false, 2, BookingConfirmationProblemCodes.DraftUnpriced)]
    [InlineData(false, true, 1, BookingConfirmationProblemCodes.VersionConflict)]
    public async Task ConfirmAsync_WithInvalidDraftState_FailsClosed(
        bool requiresReprice,
        bool hasPricing,
        int expectedVersion,
        string expectedCode)
    {
        var fixture = await CreateFixtureAsync();
        fixture.Draft.RequiresReprice = requiresReprice;
        if (!hasPricing)
        {
            fixture.Draft.PricingSnapshot = null;
        }

        var action = () => fixture.Service.ConfirmAsync(
            fixture.Draft.OrderGuid,
            ValidRequest(expectedVersion),
            fixture.DeviceId,
            fixture.UserId,
            "ar",
            null,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingConfirmationException>();
        exception.Which.Code.Should().Be(expectedCode);
        fixture.BusinessClient.CreateReservationRequests.Should().Be(0);
    }

    [Fact]
    public async Task ConfirmAsync_WhenBusinessOutcomeIsUnavailable_PersistsNothingAndAllowsSameKeyRetry()
    {
        var fixture = await CreateFixtureAsync();
        fixture.BusinessClient.CreateReservationHandler = (_, _, _) =>
            throw new BusinessApiUnavailableException("Unavailable.", "correlation");

        var action = () => fixture.Service.ConfirmAsync(
            fixture.Draft.OrderGuid,
            ValidRequest(fixture.Draft.PublicVersion),
            fixture.DeviceId,
            fixture.UserId,
            "ar",
            null,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingConfirmationException>();
        exception.Which.Code.Should().Be(BookingConfirmationProblemCodes.Unavailable);
        (await fixture.Context.CustomerBookings.CountAsync()).Should().Be(0);
        (await fixture.Context.BookingConfirmationAttempts.CountAsync()).Should().Be(1);

        var firstRequest = fixture.BusinessClient.ReservationRequests.Single();
        fixture.BusinessClient.CreateReservationHandler = (request, _, _) =>
            Task.FromResult(new CreateReservationResponse
            {
                BookingReference = request.BookingReference,
                ReservationId = Guid.NewGuid(),
                WorkOrderId = Guid.NewGuid(),
                Status = ReservationStatuses.Reserved,
                CatalogVersion = request.ExpectedCatalogVersion,
                Currency = request.Currency,
                ItemSubtotal = request.ExpectedItemSubtotal,
                TotalDurationMinutes = request.ExpectedTotalDurationMinutes,
                RequestedSlotStartUtc = request.RequestedSlotStartUtc,
                RequestedSlotEndUtc = request.RequestedSlotStartUtc.AddMinutes(
                    request.ExpectedTotalDurationMinutes),
                Items = request.Items.Select(item => new ReservationAcceptedItem
                {
                    OfferingId = item.OfferingId,
                    BaseSubtotal = item.ExpectedBaseSubtotal,
                    AddonSubtotal = item.ExpectedAddonSubtotal,
                    ItemSubtotal = item.ExpectedItemSubtotal,
                    TotalDurationMinutes = item.ExpectedDurationMinutes,
                    Selections =
                    [
                        new NormalizedAddonSelection
                        {
                            AddonGroupId = fixture.Draft.PricingSnapshot!.Items.Single()
                                .Selections.Single().AddonGroupSourceId,
                            AddonChoiceId = item.SelectedAddons.Single().AddonChoiceId,
                            SelectionType = "Single",
                            Quantity = item.SelectedAddons.Single().Quantity,
                            UnitPriceAdjustment = 10m,
                            TotalPriceAdjustment = 10m,
                            UnitDurationAdjustmentMinutes = 15,
                            TotalDurationAdjustmentMinutes = 15
                        }
                    ]
                }).ToArray()
            });

        var recovered = await fixture.Service.ConfirmAsync(
            fixture.Draft.OrderGuid,
            ValidRequest(fixture.Draft.PublicVersion),
            fixture.DeviceId,
            fixture.UserId,
            "ar",
            null,
            CancellationToken.None);

        recovered.Reference.Should().Be(firstRequest.Request.BookingReference);
        fixture.BusinessClient.ReservationRequests.Should().HaveCount(2);
        fixture.BusinessClient.ReservationRequests[1].IdempotencyKey
            .Should().Be(firstRequest.IdempotencyKey);
        fixture.BusinessClient.ReservationRequests[1].Request.BookingReference
            .Should().Be(firstRequest.Request.BookingReference);
    }

    [Fact]
    public async Task ConfirmAsync_WhenSuccessfulResponseHasMismatchedSelection_FailsClosedAndKeepsClaimForReplay()
    {
        var fixture = await CreateFixtureAsync();
        var originalHandler = fixture.BusinessClient.CreateReservationHandler;
        fixture.BusinessClient.CreateReservationHandler = async (request, key, token) =>
        {
            var response = await originalHandler(request, key, token);
            response.Items.Single().Selections.Single().Quantity++;
            return response;
        };

        var action = () => fixture.Service.ConfirmAsync(
            fixture.Draft.OrderGuid,
            ValidRequest(fixture.Draft.PublicVersion),
            fixture.DeviceId,
            fixture.UserId,
            "ar",
            null,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingConfirmationException>();
        exception.Which.Code.Should().Be(BookingConfirmationProblemCodes.Unavailable);
        fixture.Draft.ConfirmationClaimedVersion.Should().Be(fixture.Draft.PublicVersion);
        (await fixture.Context.BookingConfirmationAttempts.CountAsync()).Should().Be(1);
        (await fixture.Context.CustomerBookings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ConfirmAsync_WithReorderedMultiItemResponse_PersistsDisplayOrderAndReplays()
    {
        var fixture = await CreateMultiItemFixtureAsync();
        fixture.BusinessClient.CreateReservationHandler = (request, _, _) =>
            Task.FromResult(CreateAcceptedResponse(fixture.Draft, request, reorder: true));

        var first = await fixture.Service.ConfirmAsync(
            fixture.Draft.OrderGuid,
            ValidRequest(fixture.Draft.PublicVersion),
            fixture.DeviceId,
            fixture.UserId,
            "ar",
            null,
            CancellationToken.None);
        var replay = await fixture.Service.ConfirmAsync(
            fixture.Draft.OrderGuid,
            ValidRequest(fixture.Draft.PublicVersion),
            fixture.DeviceId,
            fixture.UserId,
            "ar",
            null,
            CancellationToken.None);

        first.Reference.Should().Be(replay.Reference);
        fixture.BusinessClient.CreateReservationRequests.Should().Be(1);
        var stored = await fixture.Context.CustomerBookings
            .AsNoTracking()
            .Include(booking => booking.Items)
                .ThenInclude(item => item.Selections)
            .SingleAsync();
        stored.Items.OrderBy(item => item.DisplayOrder)
            .Select(item => item.OfferingSourceId)
            .Should().Equal(fixture.Draft.PricingSnapshot!.Items
                .OrderBy(item => item.DisplayOrder)
                .Select(item => item.OfferingSourceId));
        foreach (var item in stored.Items)
        {
            var expected = fixture.Draft.PricingSnapshot.Items
                .Single(value => value.OfferingSourceId == item.OfferingSourceId);
            item.Selections.OrderBy(selection => selection.DisplayOrder)
                .Select(selection => selection.AddonChoiceSourceId)
                .Should().Equal(expected.Selections
                    .OrderBy(selection => selection.DisplayOrder)
                    .Select(selection => selection.AddonChoiceSourceId));
        }
    }

    [Fact]
    public async Task ConfirmAsync_WhenRetryReceivesReorderedAcceptedResponse_RecoversClaimAndPersistsBooking()
    {
        var fixture = await CreateMultiItemFixtureAsync();
        fixture.BusinessClient.CreateReservationHandler = (_, _, _) =>
            throw new BusinessApiUnavailableException("Unavailable.", "correlation");

        var firstAttempt = () => fixture.Service.ConfirmAsync(
            fixture.Draft.OrderGuid,
            ValidRequest(fixture.Draft.PublicVersion),
            fixture.DeviceId,
            fixture.UserId,
            "ar",
            null,
            CancellationToken.None);
        await firstAttempt.Should().ThrowAsync<BookingConfirmationException>();
        var claimedRequest = fixture.BusinessClient.ReservationRequests.Single();

        fixture.BusinessClient.CreateReservationHandler = (request, _, _) =>
            Task.FromResult(CreateAcceptedResponse(fixture.Draft, request, reorder: true));
        var recovered = await fixture.Service.ConfirmAsync(
            fixture.Draft.OrderGuid,
            ValidRequest(fixture.Draft.PublicVersion),
            fixture.DeviceId,
            fixture.UserId,
            "ar",
            null,
            CancellationToken.None);

        recovered.Reference.Should().Be(claimedRequest.Request.BookingReference);
        (await fixture.Context.CustomerBookings.CountAsync()).Should().Be(1);
        fixture.BusinessClient.ReservationRequests.Should().HaveCount(2);
        fixture.BusinessClient.ReservationRequests[1].IdempotencyKey
            .Should().Be(claimedRequest.IdempotencyKey);
    }

    [Theory]
    [InlineData("duplicate-item")]
    [InlineData("missing-item")]
    [InlineData("extra-item")]
    [InlineData("mismatched-item")]
    [InlineData("duplicate-selection")]
    [InlineData("missing-selection")]
    [InlineData("extra-selection")]
    [InlineData("mismatched-selection")]
    public async Task ConfirmAsync_WithMalformedMultiItemResponse_FailsClosed(string mutation)
    {
        var fixture = await CreateMultiItemFixtureAsync();
        fixture.BusinessClient.CreateReservationHandler = (request, _, _) =>
        {
            var response = CreateAcceptedResponse(fixture.Draft, request, reorder: true);
            MutateResponse(response, mutation);
            return Task.FromResult(response);
        };

        var action = () => fixture.Service.ConfirmAsync(
            fixture.Draft.OrderGuid,
            ValidRequest(fixture.Draft.PublicVersion),
            fixture.DeviceId,
            fixture.UserId,
            "ar",
            null,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingConfirmationException>();
        exception.Which.Code.Should().Be(BookingConfirmationProblemCodes.Unavailable);
        (await fixture.Context.CustomerBookings.CountAsync()).Should().Be(0);
        (await fixture.Context.BookingConfirmationAttempts.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(ReservationErrorCodes.CatalogChanged)]
    [InlineData(ReservationErrorCodes.PriceChanged)]
    public async Task ConfirmAsync_WhenBusinessRejectsPriceProof_InvalidatesPricingAndPreservesCode(
        string businessCode)
    {
        var fixture = await CreateFixtureAsync();
        fixture.BusinessClient.CreateReservationHandler = (_, _, _) =>
            throw new BusinessApiConflictException("changed", "correlation", businessCode);

        var action = () => fixture.Service.ConfirmAsync(
            fixture.Draft.OrderGuid,
            ValidRequest(fixture.Draft.PublicVersion),
            fixture.DeviceId,
            fixture.UserId,
            "ar",
            null,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingConfirmationException>();
        exception.Which.Code.Should().Be(BookingConfirmationProblemCodes.DraftRequiresReprice);
        exception.Which.BusinessErrorCode.Should().Be(businessCode);
        fixture.Draft.RequiresReprice.Should().BeTrue();
        fixture.Draft.PricingSnapshot.Should().BeNull();
        fixture.Draft.ConfirmationClaimedVersion.Should().BeNull();
        (await fixture.Context.BookingConfirmationAttempts.CountAsync()).Should().Be(0);
    }

    private static ConfirmBookingFromDraftRequest ValidRequest(int version) =>
        new()
        {
            ExpectedVersion = version,
            CancellationPolicyAcknowledged = true
        };

    private static async Task<Fixture> CreateMultiItemFixtureAsync()
    {
        var fixture = await CreateFixtureAsync();
        var pricing = fixture.Draft.PricingSnapshot!;
        var firstItem = pricing.Items.Single();
        firstItem.DisplayOrder = 1;
        firstItem.Selections.Single().DisplayOrder = 1;
        var secondChoiceId = Guid.NewGuid();
        var secondGroupId = Guid.NewGuid();
        firstItem.Selections.Add(new CheckoutDraftPricingSelectionSnapshot
        {
            AddonGroupSourceId = secondGroupId,
            AddonChoiceSourceId = secondChoiceId,
            SelectionType = "Multiple",
            Quantity = 2,
            UnitPriceAdjustment = 3m,
            TotalPriceAdjustment = 6m,
            UnitDurationAdjustmentMinutes = 2,
            TotalDurationAdjustmentMinutes = 4,
            DisplayOrder = 0
        });
        firstItem.AddonSubtotal = 16m;
        firstItem.ItemSubtotal = 116m;
        firstItem.TotalDurationMinutes = 49;

        var otherOfferingId = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();
        var otherChoiceId = Guid.NewGuid();
        pricing.Items.Add(new CheckoutDraftPricingItemSnapshot
        {
            OfferingSourceId = otherOfferingId,
            DisplayOrder = 0,
            BaseSubtotal = 50m,
            AddonSubtotal = 5m,
            ItemSubtotal = 55m,
            TotalDurationMinutes = 25,
            Selections =
            [
                new CheckoutDraftPricingSelectionSnapshot
                {
                    AddonGroupSourceId = otherGroupId,
                    AddonChoiceSourceId = otherChoiceId,
                    SelectionType = "Single",
                    Quantity = 1,
                    UnitPriceAdjustment = 5m,
                    TotalPriceAdjustment = 5m,
                    UnitDurationAdjustmentMinutes = 5,
                    TotalDurationAdjustmentMinutes = 5,
                    DisplayOrder = 0
                }
            ]
        });
        pricing.BaseSubtotal = 150m;
        pricing.AddonSubtotal = 21m;
        pricing.ItemSubtotal = 171m;
        pricing.TaxableSubtotal = 176m;
        pricing.Tax = 17.6m;
        pricing.GrandTotal = 193.6m;
        pricing.TotalDurationMinutes = 74;

        var firstOffering = fixture.Provider.Categories.Single().Offerings.Single();
        firstOffering.AddonGroups.Add(new CatalogAddonGroupReadModel
        {
            SourceAddonGroupId = secondGroupId,
            NameAr = "Second Group AR",
            SelectionType = "Multiple",
            Choices =
            [
                new CatalogAddonChoiceReadModel
                {
                    SourceAddonChoiceId = secondChoiceId,
                    NameAr = "Second Choice AR"
                }
            ]
        });
        fixture.Provider.Categories.Single().Offerings.Add(new CatalogOfferingReadModel
        {
            SourceOfferingId = otherOfferingId,
            NameAr = "Other Service AR",
            AddonGroups =
            [
                new CatalogAddonGroupReadModel
                {
                    SourceAddonGroupId = otherGroupId,
                    NameAr = "Other Group AR",
                    SelectionType = "Single",
                    Choices =
                    [
                        new CatalogAddonChoiceReadModel
                        {
                            SourceAddonChoiceId = otherChoiceId,
                            NameAr = "Other Choice AR"
                        }
                    ]
                }
            ]
        });
        fixture.Context.ChangeTracker.AcceptAllChanges();
        return fixture;
    }

    private static CreateReservationResponse CreateAcceptedResponse(
        CheckoutDraft draft,
        CreateReservationRequest request,
        bool reorder)
    {
        var snapshots = draft.PricingSnapshot!.Items.ToDictionary(item => item.OfferingSourceId);
        var items = request.Items.Select(item =>
        {
            var snapshot = snapshots[item.OfferingId];
            var selections = snapshot.Selections.Select(selection => new NormalizedAddonSelection
            {
                AddonGroupId = selection.AddonGroupSourceId,
                AddonChoiceId = selection.AddonChoiceSourceId,
                SelectionType = selection.SelectionType,
                Quantity = selection.Quantity,
                UnitPriceAdjustment = selection.UnitPriceAdjustment,
                TotalPriceAdjustment = selection.TotalPriceAdjustment,
                UnitDurationAdjustmentMinutes = selection.UnitDurationAdjustmentMinutes,
                TotalDurationAdjustmentMinutes = selection.TotalDurationAdjustmentMinutes,
                IsDefaultApplied = selection.IsDefaultApplied
            }).ToArray();
            return new ReservationAcceptedItem
            {
                OfferingId = item.OfferingId,
                BaseSubtotal = item.ExpectedBaseSubtotal,
                AddonSubtotal = item.ExpectedAddonSubtotal,
                ItemSubtotal = item.ExpectedItemSubtotal,
                TotalDurationMinutes = item.ExpectedDurationMinutes,
                Selections = reorder ? selections.Reverse().ToArray() : selections
            };
        }).ToArray();

        return new CreateReservationResponse
        {
            BookingReference = request.BookingReference,
            ReservationId = Guid.NewGuid(),
            WorkOrderId = Guid.NewGuid(),
            Status = ReservationStatuses.Reserved,
            CatalogVersion = request.ExpectedCatalogVersion,
            Currency = request.Currency,
            ItemSubtotal = request.ExpectedItemSubtotal,
            TotalDurationMinutes = request.ExpectedTotalDurationMinutes,
            RequestedSlotStartUtc = request.RequestedSlotStartUtc,
            RequestedSlotEndUtc = request.RequestedSlotStartUtc.AddMinutes(
                request.ExpectedTotalDurationMinutes),
            Items = reorder ? items.Reverse().ToArray() : items
        };
    }

    private static void MutateResponse(CreateReservationResponse response, string mutation)
    {
        var items = response.Items.ToList();
        switch (mutation)
        {
            case "duplicate-item":
                items[1] = items[0];
                break;
            case "missing-item":
                items.RemoveAt(0);
                break;
            case "extra-item":
                items.Add(items[0]);
                break;
            case "mismatched-item":
                items[0].ItemSubtotal++;
                break;
            case "duplicate-selection":
            {
                var selections = items[0].Selections.ToList();
                selections.Add(selections[0]);
                items[0].Selections = selections;
                break;
            }
            case "missing-selection":
                items.Single(item => item.Selections.Count > 1).Selections =
                    items.Single(item => item.Selections.Count > 1).Selections.Skip(1).ToArray();
                break;
            case "extra-selection":
            {
                var selections = items[0].Selections.ToList();
                selections.Add(new NormalizedAddonSelection
                {
                    AddonGroupId = Guid.NewGuid(),
                    AddonChoiceId = Guid.NewGuid(),
                    SelectionType = "Single",
                    Quantity = 1
                });
                items[0].Selections = selections;
                break;
            }
            case "mismatched-selection":
                items[0].Selections.First().Quantity++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        response.Items = items;
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var now = new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero);
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var offeringId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var choiceId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var context = new ApplicationDbContext(options);
        context.Users.Add(new User
        {
            Id = userId,
            UserName = "customer@example.com",
            Email = "customer@example.com",
            FullName = "Customer",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var pricing = new CheckoutDraftPricingSnapshot
        {
            Id = Guid.NewGuid(),
            CatalogVersion = 7,
            Currency = "ILS",
            QuotedAtUtc = now,
            BaseSubtotal = 100m,
            AddonSubtotal = 10m,
            ItemSubtotal = 110m,
            ServiceFee = 5m,
            ServiceFeeMode = "Flat",
            ServiceFeeFlatAmount = 5m,
            TaxableSubtotal = 115m,
            TaxRatePercent = 10m,
            TaxAppliesToServiceFee = true,
            Tax = 11m,
            GrandTotal = 126m,
            TotalDurationMinutes = 45,
            Items =
            [
                new CheckoutDraftPricingItemSnapshot
                {
                    Id = Guid.NewGuid(),
                    OfferingSourceId = offeringId,
                    DisplayOrder = 0,
                    BaseSubtotal = 100m,
                    AddonSubtotal = 10m,
                    ItemSubtotal = 110m,
                    TotalDurationMinutes = 45,
                    Selections =
                    [
                        new CheckoutDraftPricingSelectionSnapshot
                        {
                            Id = Guid.NewGuid(),
                            AddonGroupSourceId = groupId,
                            AddonChoiceSourceId = choiceId,
                            SelectionType = "Single",
                            Quantity = 1,
                            UnitPriceAdjustment = 10m,
                            TotalPriceAdjustment = 10m,
                            UnitDurationAdjustmentMinutes = 15,
                            TotalDurationAdjustmentMinutes = 15,
                            DisplayOrder = 0
                        }
                    ]
                }
            ]
        };
        var draft = new CheckoutDraft
        {
            Id = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            OwnerDeviceId = deviceId,
            BusinessSourceId = Guid.NewGuid(),
            BranchSourceId = branchId,
            CatalogVersion = 7,
            PublicVersion = 2,
            RequestedSlotStartUtc = now.AddHours(2),
            VehicleType = "Sedan",
            AddressLine = "Street 1",
            Latitude = 32.1m,
            Longitude = 34.8m,
            RequiresReprice = false,
            ExpiresAt = now.AddMinutes(30),
            PricingSnapshot = pricing
        };
        context.CheckoutDrafts.Add(draft);
        await context.SaveChangesAsync();

        var provider = new CatalogProviderReadModel
        {
            SourceCompanyId = draft.BusinessSourceId,
            IsEnabled = true,
            CatalogVersion = 7,
            NameAr = "Provider AR",
            NameHe = "Provider HE",
            Branches =
            [
                new CatalogBranchReadModel
                {
                    SourceBranchId = branchId,
                    NameAr = "Branch AR",
                    NameHe = "Branch HE",
                    AddressAr = "Address"
                }
            ],
            Categories =
            [
                new CatalogCategoryReadModel
                {
                    SourceCategoryId = Guid.NewGuid(),
                    NameAr = "Category",
                    Offerings =
                    [
                        new CatalogOfferingReadModel
                        {
                            SourceOfferingId = offeringId,
                            NameAr = "Service AR",
                            NameHe = "Service HE",
                            AddonGroups =
                            [
                                new CatalogAddonGroupReadModel
                                {
                                    SourceAddonGroupId = groupId,
                                    NameAr = "Group AR",
                                    NameHe = "Group HE",
                                    SelectionType = "Single",
                                    Choices =
                                    [
                                        new CatalogAddonChoiceReadModel
                                        {
                                            SourceAddonChoiceId = choiceId,
                                            NameAr = "Choice AR",
                                            NameHe = "Choice HE"
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var draftRepository = new CheckoutDraftRepository(context);
        var catalogRepository = new Mock<ICatalogReadModelRepository>();
        catalogRepository.Setup(repository =>
                repository.GetEnabledProviderBySourceCompanyIdWithGraphAsync(
                    draft.BusinessSourceId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);
        var businessClient = new ScriptedBusinessApiClient
        {
            CreateReservationHandler = (request, _, _) =>
                Task.FromResult(new CreateReservationResponse
                {
                    BookingReference = request.BookingReference,
                    ReservationId = Guid.NewGuid(),
                    WorkOrderId = Guid.NewGuid(),
                    Status = ReservationStatuses.Reserved,
                    CatalogVersion = request.ExpectedCatalogVersion,
                    Currency = request.Currency,
                    ItemSubtotal = request.ExpectedItemSubtotal,
                    TotalDurationMinutes = request.ExpectedTotalDurationMinutes,
                    RequestedSlotStartUtc = request.RequestedSlotStartUtc,
                    RequestedSlotEndUtc = request.RequestedSlotStartUtc.AddMinutes(
                        request.ExpectedTotalDurationMinutes),
                    Items = request.Items.Select(item => new ReservationAcceptedItem
                    {
                        OfferingId = item.OfferingId,
                        BaseSubtotal = item.ExpectedBaseSubtotal,
                        AddonSubtotal = item.ExpectedAddonSubtotal,
                        ItemSubtotal = item.ExpectedItemSubtotal,
                        TotalDurationMinutes = item.ExpectedDurationMinutes,
                        Selections =
                        [
                            new NormalizedAddonSelection
                            {
                                AddonGroupId = groupId,
                                AddonChoiceId = choiceId,
                                SelectionType = "Single",
                                Quantity = 1,
                                UnitPriceAdjustment = 10m,
                                TotalPriceAdjustment = 10m,
                                UnitDurationAdjustmentMinutes = 15,
                                TotalDurationAdjustmentMinutes = 15
                            }
                        ]
                    }).ToArray()
                })
        };
        var service = new BookingConfirmationService(
            context,
            draftRepository,
            catalogRepository.Object,
            businessClient,
            new ManualTimeProvider(now),
            new TestAppLogger());

        return new Fixture(context, service, businessClient, draft, provider, userId, deviceId);
    }

    private sealed record Fixture(
        ApplicationDbContext Context,
        BookingConfirmationService Service,
        ScriptedBusinessApiClient BusinessClient,
        CheckoutDraft Draft,
        CatalogProviderReadModel Provider,
        Guid UserId,
        Guid DeviceId);
}
