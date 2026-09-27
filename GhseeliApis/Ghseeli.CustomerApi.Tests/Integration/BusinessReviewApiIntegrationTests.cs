using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.DataPartitioning;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Exercises the real Customer HTTP pipeline for completed-booking reviews.
/// </summary>
public sealed class BusinessReviewApiIntegrationTests
{
    [Fact]
    public async Task Owned_create_read_update_delete_flow_has_expected_statuses()
    {
        var deviceToken = CatalogTestSupport.CreateToken(88);
        var device = CatalogTestSupport.CreateDevice(deviceToken);
        var provider = Provider(Guid.NewGuid(), Guid.NewGuid(), false);
        await using var factory = new CatalogApiFactory(
            devices: [device],
            providers: [],
            catalogProviders: [provider]);
        using var client = factory.CreateApiClient();
        var seeded = await SeedOwnedAsync(factory, device.Id, provider.SourceCompanyId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateJwt(seeded.User.Id));
        client.DefaultRequestHeaders.Add("X-Device-Token", deviceToken);

        using var create = await client.PutAsJsonAsync(
            $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
            new { rating = 5, comment = "   ", expectedRowVersion = (string?)null });
        using var createJson = await JsonDocument.ParseAsync(
            await create.Content.ReadAsStreamAsync());
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        create.Headers.CacheControl!.NoStore.Should().BeTrue();
        createJson.RootElement.GetProperty("comment").ValueKind.Should().Be(JsonValueKind.Null);
        (await ReviewStateAsync(factory, provider.SourceCompanyId))
            .Should().Be((1, 5m));

        using var read = await client.GetAsync(
            $"/api/v1/bookings/{seeded.Booking.Id:D}/review?language=he");
        read.StatusCode.Should().Be(HttpStatusCode.OK);

        await SetRowVersionAsync(factory, seeded.Booking.Id, [1, 2, 3, 4, 5, 6, 7, 8]);
        using var update = await client.PutAsJsonAsync(
            $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
            new
            {
                rating = 4,
                comment = "Updated.",
                expectedRowVersion = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8])
            });
        using var updateJson = await JsonDocument.ParseAsync(
            await update.Content.ReadAsStreamAsync());
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedRowVersion = updateJson.RootElement.GetProperty("rowVersion").GetString()!;
        (await ReviewStateAsync(factory, provider.SourceCompanyId))
            .Should().Be((1, 4m));

        using var stale = await client.PutAsJsonAsync(
            $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
            new
            {
                rating = 3,
                comment = "Stale.",
                expectedRowVersion = Convert.ToBase64String([8, 7, 6, 5, 4, 3, 2, 1])
            });
        using var staleJson = await JsonDocument.ParseAsync(
            await stale.Content.ReadAsStreamAsync());
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        staleJson.RootElement.GetProperty("code").GetString()
            .Should().Be("review_version_conflict");

        using var delete = await client.DeleteAsync(
            $"/api/v1/bookings/{seeded.Booking.Id:D}/review" +
            $"?expectedRowVersion={Uri.EscapeDataString(updatedRowVersion)}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var deleted = await client.GetAsync(
            $"/api/v1/bookings/{seeded.Booking.Id:D}/review");
        deleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReviewStateAsync(factory, provider.SourceCompanyId))
            .Should().Be((0, 0m));
    }

    [Theory]
    [InlineData(BookingStatuses.Pending)]
    [InlineData(BookingStatuses.Confirmed)]
    [InlineData(BookingStatuses.InProgress)]
    [InlineData(BookingStatuses.Cancelled)]
    [InlineData(BookingStatuses.NoShow)]
    public async Task Owned_put_rejects_non_completed_booking(string status)
    {
        var deviceToken = CatalogTestSupport.CreateToken((byte)(90 + status.Length));
        var device = CatalogTestSupport.CreateDevice(deviceToken);
        var provider = Provider(Guid.NewGuid(), Guid.NewGuid(), false);
        await using var factory = new CatalogApiFactory(
            devices: [device], providers: [], catalogProviders: [provider]);
        using var client = factory.CreateApiClient();
        var seeded = await SeedOwnedAsync(
            factory, device.Id, provider.SourceCompanyId, status);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateJwt(seeded.User.Id));
        client.DefaultRequestHeaders.Add("X-Device-Token", deviceToken);

        using var response = await client.PutAsJsonAsync(
            $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
            new { rating = 5, comment = (string?)null, expectedRowVersion = (string?)null });
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        json.RootElement.GetProperty("code").GetString()
            .Should().Be("review_booking_not_completed");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-READS-017")]
    public async Task Public_reviews_are_anonymous_paginated_deterministic_and_private()
    {
        var provider = Provider(Guid.NewGuid(), Guid.NewGuid(), false);
        await using var factory = new CatalogApiFactory(
            providers: [], catalogProviders: [provider]);
        using var client = factory.CreateApiClient();
        await SeedPublicAsync(factory, provider.SourceCompanyId);

        using var firstPage = await client.GetAsync(
            $"/api/v1/catalog/businesses/{provider.Id:D}/reviews?page=1&pageSize=1&language=ar");
        using var firstJson = await JsonDocument.ParseAsync(
            await firstPage.Content.ReadAsStreamAsync());
        using var secondPage = await client.GetAsync(
            $"/api/v1/catalog/businesses/{provider.Id:D}/reviews?page=2&pageSize=1");
        using var secondJson = await JsonDocument.ParseAsync(
            await secondPage.Content.ReadAsStreamAsync());

        firstPage.StatusCode.Should().Be(HttpStatusCode.OK);
        firstJson.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        firstJson.RootElement.GetProperty("averageRating").GetDecimal().Should().Be(4.5m);
        var first = firstJson.RootElement.GetProperty("items")[0];
        var second = secondJson.RootElement.GetProperty("items")[0];
        first.GetProperty("id").GetGuid().Should().NotBe(second.GetProperty("id").GetGuid());
        first.GetProperty("customerDisplayName").GetString().Should().MatchRegex("^[A-Z]\\*\\*\\*$");
        var serialized = first.GetRawText();
        serialized.Should().NotContainAny(
            "bookingId", "userId", "email", "phone", "address",
            "licensePlate", "rowVersion");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-DEMO-022")]
    public async Task Public_reviews_respect_production_and_demo_partitions()
    {
        var sourceId = Guid.NewGuid();
        var productionProvider = Provider(Guid.NewGuid(), sourceId, false);
        var demoProvider = Provider(Guid.NewGuid(), sourceId, true);
        var demoToken = CatalogTestSupport.CreateToken(122);
        var demoDevice = CatalogTestSupport.CreateDevice(demoToken);
        await using var factory = new CatalogApiFactory(
            providers: [],
            catalogProviders: [productionProvider]);
        using var client = factory.CreateApiClient();
        await SeedDemoDeviceAsync(factory, demoDevice);
        await SeedPublicAsync(factory, sourceId, ratings: [5], isDemo: false);
        await SeedDemoProviderAndReviewsAsync(factory, demoProvider, ratings: [2, 3]);

        using var production = await client.GetAsync(
            $"/api/v1/catalog/businesses/{productionProvider.Id:D}/reviews");
        using var productionJson = await JsonDocument.ParseAsync(
            await production.Content.ReadAsStreamAsync());
        using var demoRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/catalog/businesses/{demoProvider.Id:D}/reviews");
        demoRequest.Headers.Add("X-Device-Token", demoToken);
        using var demo = await client.SendAsync(demoRequest);
        using var demoJson = await JsonDocument.ParseAsync(
            await demo.Content.ReadAsStreamAsync());

        productionJson.RootElement.GetProperty("ratingCount").GetInt32().Should().Be(1);
        productionJson.RootElement.GetProperty("averageRating").GetDecimal().Should().Be(5m);
        demoJson.RootElement.GetProperty("ratingCount").GetInt32().Should().Be(2);
        demoJson.RootElement.GetProperty("averageRating").GetDecimal().Should().Be(2.5m);
    }

    [Theory]
    [Trait("ScenarioId", "FAN-OPTIONAL-INVALID-035")]
    [InlineData("malformed")]
    [InlineData("unknown")]
    public async Task Public_reviews_reject_malformed_or_unknown_optional_device(
        string condition)
    {
        var provider = Provider(Guid.NewGuid(), Guid.NewGuid(), false);
        await using var factory = new CatalogApiFactory(
            providers: [], catalogProviders: [provider]);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/catalog/businesses/{provider.Id:D}/reviews");
        request.Headers.Add(
            "X-Device-Token",
            condition == "malformed" ? "bad" : CatalogTestSupport.CreateToken(199));

        using var response = await client.SendAsync(request);
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        json.RootElement.GetProperty("code").GetString()
            .Should().Be("device_token_invalid");
        json.RootElement.TryGetProperty("items", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Foreign_and_missing_put_delete_are_identical_and_do_not_mutate_reviews_or_aggregates()
    {
        var token = CatalogTestSupport.CreateToken(123);
        var device = CatalogTestSupport.CreateDevice(token);
        var provider = Provider(Guid.NewGuid(), Guid.NewGuid(), false);
        await using var factory = new CatalogApiFactory(
            devices: [device], providers: [], catalogProviders: [provider]);
        var owner = await SeedOwnedAsync(factory, device.Id, provider.SourceCompanyId);
        var caller = await SeedOwnedAsync(factory, device.Id, provider.SourceCompanyId);
        await SeedReviewAsync(factory, owner.User, owner.Booking, 5);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateJwt(caller.User.Id));
        client.DefaultRequestHeaders.Add("X-Device-Token", token);
        var missingId = Guid.NewGuid();
        var before = await ReviewStateAsync(factory, provider.SourceCompanyId);

        foreach (var bookingId in new[] { owner.Booking.Id, missingId })
        {
            using var put = await client.PutAsJsonAsync(
                $"/api/v1/bookings/{bookingId:D}/review",
                new { rating = 1, comment = "must not persist", expectedRowVersion = (string?)null });
            using var putJson = await JsonDocument.ParseAsync(
                await put.Content.ReadAsStreamAsync());
            put.StatusCode.Should().Be(HttpStatusCode.NotFound);
            putJson.RootElement.GetProperty("code").GetString().Should().Be("booking_not_found");

            using var delete = await client.DeleteAsync(
                $"/api/v1/bookings/{bookingId:D}/review");
            using var deleteJson = await JsonDocument.ParseAsync(
                await delete.Content.ReadAsStreamAsync());
            delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
            deleteJson.RootElement.GetProperty("code").GetString().Should().Be("booking_not_found");
        }

        (await ReviewStateAsync(factory, provider.SourceCompanyId)).Should().Be(before);
    }

    [Fact]
    public async Task Owned_routes_enforce_customer_bearer_and_required_device_at_the_actual_middleware_boundary()
    {
        var token = CatalogTestSupport.CreateToken(124);
        var device = CatalogTestSupport.CreateDevice(token);
        var provider = Provider(Guid.NewGuid(), Guid.NewGuid(), false);
        await using var factory = new CatalogApiFactory(
            devices: [device], providers: [], catalogProviders: [provider]);
        var seeded = await SeedOwnedAsync(factory, device.Id, provider.SourceCompanyId);

        using (var anonymous = factory.CreateApiClient())
        using (var response = await anonymous.GetAsync(
                   $"/api/v1/bookings/{seeded.Booking.Id:D}/review"))
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await ProblemCodeAsync(response)).Should().Be("device_token_missing");
        }

        using (var missingDevice = factory.CreateApiClient())
        {
            missingDevice.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateJwt(seeded.User.Id));
            using var response = await missingDevice.DeleteAsync(
                $"/api/v1/bookings/{seeded.Booking.Id:D}/review");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await ProblemCodeAsync(response)).Should().Be("device_token_missing");
        }

        using (var businessJwt = factory.CreateApiClient())
        {
            businessJwt.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateJwt(
                    seeded.User.Id, secret: "BusinessApiTestsSecret_Minimum32Chars"));
            businessJwt.DefaultRequestHeaders.Add("X-Device-Token", token);
            using var response = await businessJwt.PutAsJsonAsync(
                $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
                new { rating = 5, expectedRowVersion = (string?)null });
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Expected_rowversion_boundaries_are_stable_over_http()
    {
        var token = CatalogTestSupport.CreateToken(125);
        var device = CatalogTestSupport.CreateDevice(token);
        var provider = Provider(Guid.NewGuid(), Guid.NewGuid(), false);
        await using var factory = new CatalogApiFactory(
            devices: [device], providers: [], catalogProviders: [provider]);
        var seeded = await SeedOwnedAsync(factory, device.Id, provider.SourceCompanyId);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateJwt(seeded.User.Id));
        client.DefaultRequestHeaders.Add("X-Device-Token", token);

        using var suppliedOnCreate = await client.PutAsJsonAsync(
            $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
            new
            {
                rating = 5,
                expectedRowVersion = Convert.ToBase64String(new byte[8])
            });
        suppliedOnCreate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(suppliedOnCreate)).Should().Be("review_version_conflict");

        using var created = await client.PutAsJsonAsync(
            $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
            new { rating = 5, expectedRowVersion = (string?)null });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        await SetRowVersionAsync(factory, seeded.Booking.Id, [1, 2, 3, 4, 5, 6, 7, 8]);

        using var missing = await client.PutAsJsonAsync(
            $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
            new { rating = 4, expectedRowVersion = (string?)null });
        missing.StatusCode.Should().Be(HttpStatusCode.Conflict);

        foreach (var invalid in new[] { "not-base64", Convert.ToBase64String([1, 2, 3]) })
        {
            using var response = await client.PutAsJsonAsync(
                $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
                new { rating = 4, expectedRowVersion = invalid });
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ProblemCodeAsync(response)).Should().Be("review_invalid");
        }
    }

    [Fact]
    public async Task Public_optional_device_contract_rejects_bad_tokens_and_partition_mismatch()
    {
        var sourceId = Guid.NewGuid();
        var provider = Provider(Guid.NewGuid(), sourceId, false);
        var productionToken = CatalogTestSupport.CreateToken(126);
        var productionDevice = CatalogTestSupport.CreateDevice(productionToken);
        await using var factory = new CatalogApiFactory(
            devices: [productionDevice], providers: [], catalogProviders: [provider]);
        await SeedPublicAsync(factory, sourceId, ratings: [5]);
        using var client = factory.CreateApiClient();

        using var anonymous = await client.GetAsync(
            $"/api/v1/catalog/businesses/{provider.Id:D}/reviews");
        anonymous.StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var supplied in new[] { "malformed", CatalogTestSupport.CreateToken(127) })
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/catalog/businesses/{provider.Id:D}/reviews");
            request.Headers.Add("X-Device-Token", supplied);
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await ProblemCodeAsync(response)).Should().Be("device_token_invalid");
        }

        using var mismatch = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/catalog/businesses/{provider.Id:D}/reviews");
        mismatch.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid(), DataPartitionNames.Demo));
        mismatch.Headers.Add("X-Device-Token", productionToken);
        using var mismatchResponse = await client.SendAsync(mismatch);
        mismatchResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(mismatchResponse)).Should().Be("data_partition_mismatch");
    }

    [Fact]
    public async Task Public_paging_localization_business_resolution_and_display_masking_are_complete()
    {
        var sourceId = Guid.NewGuid();
        var provider = Provider(Guid.NewGuid(), sourceId, false);
        var disabled = Provider(Guid.NewGuid(), Guid.NewGuid(), false);
        disabled.IsEnabled = false;
        var demoOnly = Provider(Guid.NewGuid(), Guid.NewGuid(), true);
        await using var factory = new CatalogApiFactory(
            providers: [], catalogProviders: [provider, disabled]);
        await SeedNamedReviewsAsync(
            factory,
            sourceId,
            ["   ", "A", "علي", "דוד", "Maya"],
            55);
        await SeedDemoProviderAndReviewsAsync(factory, demoOnly, [5]);
        using var client = factory.CreateApiClient();

        using var pageOne = await client.GetAsync(
            $"/api/v1/catalog/businesses/{provider.Id:D}/reviews?page=1&pageSize=50");
        using var pageTwo = await client.GetAsync(
            $"/api/v1/catalog/businesses/{provider.Id:D}/reviews?page=2&pageSize=50");
        var firstIds = await ReviewIdsAsync(pageOne);
        var secondIds = await ReviewIdsAsync(pageTwo);
        pageOne.StatusCode.Should().Be(HttpStatusCode.OK);
        firstIds.Should().HaveCount(50).And.NotIntersectWith(secondIds);
        secondIds.Should().HaveCount(5);

        using var masksResponse = await client.GetAsync(
            $"/api/v1/catalog/businesses/{provider.Id:D}/reviews?page=2&pageSize=50");
        using var masksJson = await JsonDocument.ParseAsync(
            await masksResponse.Content.ReadAsStreamAsync());
        masksJson.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("customerDisplayName").GetString())
            .Should().BeEquivalentTo("***", "A***", "ع***", "ד***", "M***");

        using var nonInteger = await client.GetAsync(
            $"/api/v1/catalog/businesses/{provider.Id:D}/reviews?page=text&pageSize=20");
        nonInteger.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(nonInteger)).Should().Be("pagination_invalid");

        foreach (var missingBusiness in new[] { Guid.NewGuid(), disabled.Id, demoOnly.Id })
        {
            using var ar = await client.GetAsync(
                $"/api/v1/catalog/businesses/{missingBusiness:D}/reviews?language=ar");
            using var he = await client.GetAsync(
                $"/api/v1/catalog/businesses/{missingBusiness:D}/reviews?language=he");
            ar.StatusCode.Should().Be(HttpStatusCode.NotFound);
            he.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var arBody = await ar.Content.ReadAsStringAsync();
            var heBody = await he.Content.ReadAsStringAsync();
            arBody.Should().Contain("لم يتم العثور");
            heBody.Should().Contain("לא נמצא");
        }
    }

    [Fact]
    public async Task Owned_problem_details_are_fully_localized_in_arabic_and_hebrew()
    {
        var token = CatalogTestSupport.CreateToken(128);
        var device = CatalogTestSupport.CreateDevice(token);
        await using var factory = new CatalogApiFactory(devices: [device], providers: []);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateJwt(Guid.NewGuid()));
        client.DefaultRequestHeaders.Add("X-Device-Token", token);

        using var arabic = await client.GetAsync(
            $"/api/v1/bookings/{Guid.NewGuid():D}/review?language=ar");
        using var hebrew = await client.GetAsync(
            $"/api/v1/bookings/{Guid.NewGuid():D}/review?language=he");
        var arabicBody = await arabic.Content.ReadAsStringAsync();
        var hebrewBody = await hebrew.Content.ReadAsStringAsync();
        arabicBody.Should().ContainAll(
            "تعذر إكمال عملية التقييم", "لم يتم العثور على الحجز المطلوب", "\"language\":\"ar\"");
        hebrewBody.Should().ContainAll(
            "לא ניתן להשלים את פעולת הביקורת", "ההזמנה המבוקשת לא נמצאה", "\"language\":\"he\"");
    }

    private static async Task<(User User, CustomerBooking Booking)> SeedOwnedAsync(
        CatalogApiFactory factory,
        Guid deviceId,
        Guid businessSourceId,
        string status = BookingStatuses.Completed)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = UserEntity(Guid.NewGuid(), "Owner Customer");
        var booking = Booking(user.Id, deviceId, businessSourceId, status);
        context.Users.Add(user);
        context.CustomerBookings.Add(booking);
        await context.SaveChangesAsync();
        return (user, booking);
    }

    private static async Task SetRowVersionAsync(
        CatalogApiFactory factory,
        Guid bookingId,
        byte[] rowVersion)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var review = await context.BusinessReviews.SingleAsync(
            value => value.CustomerBookingId == bookingId);
        review.RowVersion = rowVersion;
        await context.SaveChangesAsync();
    }

    private static async Task SeedPublicAsync(
        CatalogApiFactory factory,
        Guid businessSourceId,
        int[]? ratings = null,
        bool isDemo = false)
    {
        using var scope = factory.Services.CreateScope();
        var partition = scope.ServiceProvider.GetRequiredService<ICustomerDataPartitionContext>();
        if (isDemo)
        {
            partition.SetTrustedPartition(DataPartitionNames.Demo);
        }
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ratings ??= [4, 5];
        for (var index = 0; index < ratings.Length; index++)
        {
            var user = UserEntity(Guid.NewGuid(), index == 0 ? "Maya Demo" : "Omar Demo");
            var booking = Booking(
                user.Id, Guid.NewGuid(), businessSourceId, BookingStatuses.Completed);
            context.Users.Add(user);
            context.CustomerBookings.Add(booking);
            context.BusinessReviews.Add(new BusinessReview
            {
                Id = Guid.NewGuid(),
                CustomerBooking = booking,
                User = user,
                BusinessSourceId = businessSourceId,
                Rating = ratings[index],
                Comment = index == 0 ? "Great." : null,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(index),
                UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(index)
            });
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedReviewAsync(
        CatalogApiFactory factory,
        User user,
        CustomerBooking booking,
        int rating)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.BusinessReviews.Add(new BusinessReview
        {
            Id = Guid.NewGuid(),
            CustomerBookingId = booking.Id,
            UserId = user.Id,
            BusinessSourceId = booking.BusinessSourceId,
            Rating = rating,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedNamedReviewsAsync(
        CatalogApiFactory factory,
        Guid businessSourceId,
        IReadOnlyList<string?> oldestNames,
        int count)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var start = new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < count; index++)
        {
            var user = UserEntity(
                Guid.NewGuid(),
                index < oldestNames.Count ? oldestNames[index] ?? string.Empty : $"Customer {index}");
            user.FullName = index < oldestNames.Count
                ? oldestNames[index]!
                : $"Customer {index}";
            var booking = Booking(
                user.Id, Guid.NewGuid(), businessSourceId, BookingStatuses.Completed);
            context.Users.Add(user);
            context.CustomerBookings.Add(booking);
            context.BusinessReviews.Add(new BusinessReview
            {
                Id = Guid.NewGuid(),
                CustomerBooking = booking,
                User = user,
                BusinessSourceId = businessSourceId,
                Rating = (index % 5) + 1,
                CreatedAtUtc = start.AddMinutes(index),
                UpdatedAtUtc = start.AddMinutes(index)
            });
        }
        await context.SaveChangesAsync();
    }

    private static async Task<(int Count, decimal Average)> ReviewStateAsync(
        CatalogApiFactory factory,
        Guid businessSourceId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ratings = await context.BusinessReviews
            .Where(review => review.BusinessSourceId == businessSourceId)
            .Select(review => review.Rating)
            .ToArrayAsync();
        return (
            ratings.Length,
            ratings.Length == 0 ? 0m : (decimal)ratings.Average());
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("code").GetString()!;
    }

    private static async Task<Guid[]> ReviewIdsAsync(HttpResponseMessage response)
    {
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToArray();
    }

    private static async Task SeedDemoProviderAndReviewsAsync(
        CatalogApiFactory factory,
        CatalogProviderReadModel provider,
        int[] ratings)
    {
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICustomerDataPartitionContext>()
                .SetTrustedPartition(DataPartitionNames.Demo);
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.CatalogProviders.Add(provider);
            await context.SaveChangesAsync();
        }
        await SeedPublicAsync(
            factory, provider.SourceCompanyId, ratings, isDemo: true);
    }

    private static async Task SeedDemoDeviceAsync(
        CatalogApiFactory factory,
        CustomerDevice device)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICustomerDataPartitionContext>()
            .SetTrustedPartition(DataPartitionNames.Demo);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.CustomerDevices.Add(device);
        await context.SaveChangesAsync();
    }

    private static CatalogProviderReadModel Provider(
        Guid id,
        Guid sourceId,
        bool isDemo) => new()
        {
            Id = id,
            SourceCompanyId = sourceId,
            IsEnabled = true,
            IsDemo = isDemo,
            NameAr = "مزود",
            CatalogVersion = 1,
            SnapshotHash = "review",
            LastSuccessfulRefreshAtUtc = DateTimeOffset.UtcNow
        };

    private static User UserEntity(Guid id, string fullName) => new()
    {
        Id = id,
        UserName = $"{id:N}@example.test",
        NormalizedUserName = $"{id:N}@EXAMPLE.TEST",
        Email = $"{id:N}@example.test",
        NormalizedEmail = $"{id:N}@EXAMPLE.TEST",
        FullName = fullName,
        IsActive = true
    };

    private static CustomerBooking Booking(
        Guid userId,
        Guid deviceId,
        Guid businessSourceId,
        string status) => new()
        {
            Id = Guid.NewGuid(),
            PublicReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            UserId = userId,
            OwnerDeviceId = deviceId,
            BusinessReservationId = Guid.NewGuid(),
            BusinessWorkOrderId = Guid.NewGuid(),
            BusinessSourceId = businessSourceId,
            BranchSourceId = Guid.NewGuid(),
            CatalogVersion = 1,
            ConfirmedDraftVersion = 1,
            Status = status,
            StatusChangedAtUtc = DateTimeOffset.UtcNow,
            RequestedSlotStartUtc = DateTimeOffset.UtcNow,
            RequestedSlotEndUtc = DateTimeOffset.UtcNow.AddHours(1),
            ProviderNameAr = "مزود",
            BranchNameAr = "فرع",
            VehicleType = "Sedan",
            AddressLine = "Street",
            Currency = "ILS",
            ServiceFeeMode = "None",
            TotalDurationMinutes = 60,
            QuotedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

    private static string CreateJwt(
        Guid userId,
        string partition = DataPartitionNames.Production,
        string secret = "CatalogApiTestsSecret_Minimum32Chars")
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(secret));
        var token = new JwtSecurityToken(
            issuer: "GhseeliApis.CatalogTests",
            audience: "GhseeliApis.CatalogClients",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, "User"),
                new Claim(DataPartitionNames.ClaimType, partition)
            ],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(
                key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
