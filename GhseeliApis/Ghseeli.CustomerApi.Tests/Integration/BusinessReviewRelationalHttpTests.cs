using System.IdentityModel.Tokens.Jwt;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Exercises review rowversion and unique-create behavior through HTTP and SQL Server.
/// </summary>
public sealed class BusinessReviewRelationalHttpTests
{
    [Fact]
    public async Task Successful_update_returns_new_rowversion_and_timestamp_then_delete_is_physical()
    {
        await using var factory = new RelationalReviewApiFactory();
        var seeded = await factory.SeedAsync();
        using var client = factory.CreateApiClient(seeded.User.Id, seeded.DeviceToken);

        using var create = await client.PutAsJsonAsync(
            $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
            new { rating = 5, comment = "first", expectedRowVersion = (string?)null });
        using var createJson = await JsonDocument.ParseAsync(
            await create.Content.ReadAsStreamAsync());
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstVersion = createJson.RootElement.GetProperty("rowVersion").GetString()!;
        Convert.FromBase64String(firstVersion).Should().HaveCount(8);
        var createdAt = createJson.RootElement.GetProperty("createdAtUtc").GetDateTimeOffset();
        var firstUpdatedAt = createJson.RootElement.GetProperty("updatedAtUtc").GetDateTimeOffset();
        firstUpdatedAt.Should().Be(createdAt);

        await Task.Delay(20);
        using var update = await client.PutAsJsonAsync(
            $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
            new { rating = 3, comment = "updated", expectedRowVersion = firstVersion });
        using var updateJson = await JsonDocument.ParseAsync(
            await update.Content.ReadAsStreamAsync());
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondVersion = updateJson.RootElement.GetProperty("rowVersion").GetString()!;
        secondVersion.Should().NotBe(firstVersion);
        Convert.FromBase64String(secondVersion).Should().HaveCount(8);
        updateJson.RootElement.GetProperty("createdAtUtc").GetDateTimeOffset()
            .Should().Be(createdAt);
        updateJson.RootElement.GetProperty("updatedAtUtc").GetDateTimeOffset()
            .Should().BeAfter(firstUpdatedAt);

        await using (var verify = factory.CreateContext())
        {
            var aggregate = await verify.BusinessReviews
                .Where(review => review.BusinessSourceId == seeded.Booking.BusinessSourceId)
                .GroupBy(_ => 1)
                .Select(group => new { Count = group.Count(), Average = group.Average(x => x.Rating) })
                .SingleAsync();
            aggregate.Count.Should().Be(1);
            aggregate.Average.Should().Be(3d);
        }

        using var delete = await client.DeleteAsync(DeleteUrl(seeded.Booking.Id, secondVersion));
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var deleted = factory.CreateContext();
        (await deleted.BusinessReviews.IgnoreQueryFilters().CountAsync())
            .Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_updates_with_same_rowversion_return_one_200_one_409_and_new_winner_version()
    {
        await using var factory = new RelationalReviewApiFactory();
        var seeded = await factory.SeedAsync();
        using var first = factory.CreateApiClient(seeded.User.Id, seeded.DeviceToken);
        using var second = factory.CreateApiClient(seeded.User.Id, seeded.DeviceToken);
        var currentVersion = await CreateReviewAsync(first, seeded.Booking.Id, 5, "original");

        factory.SynchronizeNextReviewReads();
        var responses = await Task.WhenAll(
            first.PutAsJsonAsync(
                $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
                new { rating = 4, comment = "first update", expectedRowVersion = currentVersion }),
            second.PutAsJsonAsync(
                $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
                new { rating = 2, comment = "second update", expectedRowVersion = currentVersion }));
        try
        {
            await AssertWinnerAndVersionConflictAsync(responses, HttpStatusCode.OK);
            using var winnerJson = await JsonDocument.ParseAsync(
                await responses.Single(response => response.StatusCode == HttpStatusCode.OK)
                    .Content.ReadAsStreamAsync());
            var winnerVersion = winnerJson.RootElement.GetProperty("rowVersion").GetString()!;
            winnerVersion.Should().NotBe(currentVersion);

            await using var verify = factory.CreateContext();
            var review = await verify.BusinessReviews.SingleAsync();
            (review.Rating, review.Comment).Should().BeOneOf(
                (4, "first update"),
                (2, "second update"));
            Convert.ToBase64String(review.RowVersion).Should().Be(winnerVersion);
            await AssertPublicAndRepositoryAggregateAsync(
                factory,
                seeded.CatalogBusinessId,
                seeded.Booking.BusinessSourceId,
                review.Rating);
        }
        finally
        {
            DisposeAll(responses);
        }
    }

    [Fact]
    public async Task Concurrent_deletes_with_same_rowversion_return_one_204_one_409_and_delete_once()
    {
        await using var factory = new RelationalReviewApiFactory();
        var seeded = await factory.SeedAsync();
        using var first = factory.CreateApiClient(seeded.User.Id, seeded.DeviceToken);
        using var second = factory.CreateApiClient(seeded.User.Id, seeded.DeviceToken);
        var currentVersion = await CreateReviewAsync(first, seeded.Booking.Id, 5, "delete me");

        factory.SynchronizeNextReviewReads();
        var responses = await Task.WhenAll(
            first.DeleteAsync(DeleteUrl(seeded.Booking.Id, currentVersion)),
            second.DeleteAsync(DeleteUrl(seeded.Booking.Id, currentVersion)));
        try
        {
            await AssertWinnerAndVersionConflictAsync(responses, HttpStatusCode.NoContent);
        }
        finally
        {
            DisposeAll(responses);
        }

        await using var verify = factory.CreateContext();
        (await verify.BusinessReviews.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        var aggregate = await verify.BusinessReviews
            .Where(review => review.BusinessSourceId == seeded.Booking.BusinessSourceId)
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Average = group.Average(x => x.Rating) })
            .SingleOrDefaultAsync();
        aggregate.Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_update_and_delete_have_one_winner_stable_conflict_and_consistent_state()
    {
        await using var factory = new RelationalReviewApiFactory();
        var seeded = await factory.SeedAsync();
        using var updateClient = factory.CreateApiClient(seeded.User.Id, seeded.DeviceToken);
        using var deleteClient = factory.CreateApiClient(seeded.User.Id, seeded.DeviceToken);
        var currentVersion = await CreateReviewAsync(
            updateClient, seeded.Booking.Id, 5, "original");

        factory.SynchronizeNextReviewReads();
        var updateTask = updateClient.PutAsJsonAsync(
            $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
            new { rating = 3, comment = "updated", expectedRowVersion = currentVersion });
        var deleteTask = deleteClient.DeleteAsync(
            DeleteUrl(seeded.Booking.Id, currentVersion));
        await Task.WhenAll(updateTask, deleteTask);
        using var update = await updateTask;
        using var delete = await deleteTask;
        var responses = new[] { update, delete };

        responses.Should().NotContain(response =>
            response.StatusCode == HttpStatusCode.InternalServerError);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict)
            .Should().Be(1, "statuses were {0}",
                string.Join(", ", responses.Select(response => response.StatusCode)));
        using (var conflictJson = await JsonDocument.ParseAsync(
                   await responses.Single(response => response.StatusCode == HttpStatusCode.Conflict)
                       .Content.ReadAsStreamAsync()))
        {
            conflictJson.RootElement.GetProperty("code").GetString()
                .Should().Be("review_version_conflict");
        }

        await using var verify = factory.CreateContext();
        var finalReview = await verify.BusinessReviews.IgnoreQueryFilters().SingleOrDefaultAsync();
        if (update.StatusCode == HttpStatusCode.OK)
        {
            delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
            finalReview.Should().NotBeNull();
            finalReview!.Rating.Should().Be(3);
            finalReview.Comment.Should().Be("updated");
            Convert.ToBase64String(finalReview.RowVersion).Should().NotBe(currentVersion);
        }
        else
        {
            delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
            update.StatusCode.Should().Be(HttpStatusCode.Conflict);
            finalReview.Should().BeNull();
        }

        var aggregate = await verify.BusinessReviews
            .Where(review => review.BusinessSourceId == seeded.Booking.BusinessSourceId)
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Average = group.Average(x => x.Rating) })
            .SingleOrDefaultAsync();
        if (finalReview is null)
        {
            aggregate.Should().BeNull();
        }
        else
        {
            aggregate!.Count.Should().Be(1);
            aggregate.Average.Should().Be(3d);
        }
    }

    [Fact]
    public async Task Delete_swagger_exposes_expected_rowversion_query_precondition()
    {
        await using var factory = new RelationalReviewApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        var parameters = json.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/bookings/{bookingId}/review")
            .GetProperty("delete")
            .GetProperty("parameters");

        parameters.EnumerateArray().Should().Contain(parameter =>
            parameter.GetProperty("name").GetString() == "expectedRowVersion" &&
            parameter.GetProperty("in").GetString() == "query");
    }

    [Fact]
    public async Task Concurrent_create_returns_exactly_one_201_and_one_409_with_one_row_and_no_500()
    {
        await using var factory = new RelationalReviewApiFactory();
        var seeded = await factory.SeedAsync();
        using var first = factory.CreateApiClient(seeded.User.Id, seeded.DeviceToken);
        using var second = factory.CreateApiClient(seeded.User.Id, seeded.DeviceToken);

        var responses = await Task.WhenAll(
            first.PutAsJsonAsync(
                $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
                new { rating = 5, expectedRowVersion = (string?)null }),
            second.PutAsJsonAsync(
                $"/api/v1/bookings/{seeded.Booking.Id:D}/review",
                new { rating = 5, expectedRowVersion = (string?)null }));
        try
        {
            responses.Select(response => response.StatusCode)
                .Order()
                .Should().Equal(
                    new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }.Order());
            responses.Should().NotContain(response =>
                response.StatusCode == HttpStatusCode.InternalServerError);
            var conflict = responses.Single(response =>
                response.StatusCode == HttpStatusCode.Conflict);
            using var json = await JsonDocument.ParseAsync(
                await conflict.Content.ReadAsStreamAsync());
            json.RootElement.GetProperty("code").GetString()
                .Should().Be("review_version_conflict");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var verify = factory.CreateContext();
        (await verify.BusinessReviews.IgnoreQueryFilters().CountAsync())
            .Should().Be(1);
        await AssertPublicAndRepositoryAggregateAsync(
            factory,
            seeded.CatalogBusinessId,
            seeded.Booking.BusinessSourceId,
            5);
    }

    private sealed class RelationalReviewApiFactory :
        WebApplicationFactory<Program>,
        IAsyncDisposable
    {
        private readonly SqlServerCatalogDatabase _database =
            SqlServerCatalogDatabase.CreateAsync().GetAwaiter().GetResult();
        private readonly ReviewReadBarrierInterceptor _reviewReadBarrier = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:CustomerConnection", _database.ConnectionString);
            builder.UseSetting("JwtSettings:SecretKey", "ReviewHttpTestsSecret_Minimum32Characters");
            builder.UseSetting("JwtSettings:Issuer", "GhseeliApis.ReviewHttpTests");
            builder.UseSetting("JwtSettings:Audience", "GhseeliApis.ReviewHttpClients");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.RemoveAll<ApplicationDbContext>();
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlServer(_database.ConnectionString)
                        .AddInterceptors(_reviewReadBarrier));
            });
        }

        public void SynchronizeNextReviewReads() => _reviewReadBarrier.Arm();

        public async Task<(
            User User,
            CustomerBooking Booking,
            Guid CatalogBusinessId,
            string DeviceToken)> SeedAsync()
        {
            var token = CatalogTestSupport.CreateToken(129);
            var device = CatalogTestSupport.CreateDevice(token);
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = $"{Guid.NewGuid():N}@example.test",
                NormalizedUserName = $"{Guid.NewGuid():N}@EXAMPLE.TEST",
                Email = $"{Guid.NewGuid():N}@example.test",
                NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.TEST",
                FullName = "Review Owner",
                IsActive = true
            };
            device.UserId = user.Id;
            var booking = CreateBooking(user.Id, device.Id);
            var catalogBusiness = new CatalogProviderReadModel
            {
                Id = Guid.NewGuid(),
                SourceCompanyId = booking.BusinessSourceId,
                IsEnabled = true,
                NameAr = "مزود",
                CatalogVersion = 1,
                SnapshotHash = "review-concurrency",
                LastSuccessfulRefreshAtUtc = DateTimeOffset.UtcNow
            };
            await _database.ExecuteAsync(context =>
            {
                context.Users.Add(user);
                context.CustomerDevices.Add(device);
                context.CustomerBookings.Add(booking);
                context.CatalogProviders.Add(catalogBusiness);
            });
            return (user, booking, catalogBusiness.Id, token);
        }

        public HttpClient CreateApiClient(Guid userId, string deviceToken)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateJwt(userId));
            client.DefaultRequestHeaders.Add("X-Device-Token", deviceToken);
            return client;
        }

        public ApplicationDbContext CreateContext() => _database.CreateContext();

        public new async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _database.DisposeAsync();
        }

        private static CustomerBooking CreateBooking(Guid userId, Guid deviceId) => new()
        {
            Id = Guid.NewGuid(),
            PublicReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            UserId = userId,
            OwnerDeviceId = deviceId,
            BusinessReservationId = Guid.NewGuid(),
            BusinessWorkOrderId = Guid.NewGuid(),
            BusinessSourceId = Guid.NewGuid(),
            BranchSourceId = Guid.NewGuid(),
            CatalogVersion = 1,
            ConfirmedDraftVersion = 1,
            Status = BookingStatuses.Completed,
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

        private static string CreateJwt(Guid userId)
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("ReviewHttpTestsSecret_Minimum32Characters"));
            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer: "GhseeliApis.ReviewHttpTests",
                audience: "GhseeliApis.ReviewHttpClients",
                claims:
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, "User"),
                    new Claim(DataPartitionNames.ClaimType, DataPartitionNames.Production)
                ],
                expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: new SigningCredentials(
                    key, SecurityAlgorithms.HmacSha256)));
        }
    }

    private static async Task<string> CreateReviewAsync(
        HttpClient client,
        Guid bookingId,
        int rating,
        string comment)
    {
        using var response = await client.PutAsJsonAsync(
            $"/api/v1/bookings/{bookingId:D}/review",
            new { rating, comment, expectedRowVersion = (string?)null });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("rowVersion").GetString()!;
    }

    private static string DeleteUrl(Guid bookingId, string rowVersion) =>
        $"/api/v1/bookings/{bookingId:D}/review?expectedRowVersion={Uri.EscapeDataString(rowVersion)}";

    private static async Task AssertPublicAndRepositoryAggregateAsync(
        RelationalReviewApiFactory factory,
        Guid catalogBusinessId,
        Guid businessSourceId,
        int expectedRating)
    {
        using var publicClient = factory.CreateClient();
        using var response = await publicClient.GetAsync(
            $"/api/v1/catalog/businesses/{catalogBusinessId:D}/reviews");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("ratingCount").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("averageRating").GetDecimal()
            .Should().Be((decimal)expectedRating);

        await using var context = factory.CreateContext();
        var aggregates = await new BusinessReviewRepository(context)
            .GetAggregatesAsync([businessSourceId], default);
        aggregates[businessSourceId].RatingCount.Should().Be(1);
        aggregates[businessSourceId].AverageRating.Should().Be((decimal)expectedRating);
    }

    private static async Task AssertWinnerAndVersionConflictAsync(
        IReadOnlyCollection<HttpResponseMessage> responses,
        HttpStatusCode winnerStatus)
    {
        responses.Select(response => response.StatusCode)
            .Order()
            .Should().Equal(
                new[] { winnerStatus, HttpStatusCode.Conflict }.Order());
        responses.Should().NotContain(response =>
            response.StatusCode == HttpStatusCode.InternalServerError ||
            response.StatusCode == HttpStatusCode.NotFound);
        using var conflictJson = await JsonDocument.ParseAsync(
            await responses.Single(response => response.StatusCode == HttpStatusCode.Conflict)
                .Content.ReadAsStreamAsync());
        conflictJson.RootElement.GetProperty("code").GetString()
            .Should().Be("review_version_conflict");
    }

    private static void DisposeAll(IEnumerable<HttpResponseMessage> responses)
    {
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private sealed class ReviewReadBarrierInterceptor : DbCommandInterceptor
    {
        private TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _remaining;

        public void Arm()
        {
            _release = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _remaining, 2);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _remaining) > 0 &&
                command.CommandText.Contains("FROM [BusinessReviews]", StringComparison.Ordinal))
            {
                if (Interlocked.Decrement(ref _remaining) == 0)
                {
                    _release.TrySetResult();
                }

                await _release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
