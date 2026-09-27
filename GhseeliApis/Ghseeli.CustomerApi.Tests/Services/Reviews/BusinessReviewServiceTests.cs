using FluentAssertions;
using Ghseeli.IntegrationContracts.Bookings;
using GhseeliApis.DTOs.Reviews;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Reviews;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GhseeliApis.Tests.Services.Reviews;

/// <summary>
/// Verifies completed-booking review ownership, state, concurrency, privacy, and aggregates.
/// </summary>
public sealed class BusinessReviewServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Put_creates_trimmed_review_for_owned_completed_booking()
    {
        var booking = CompletedBooking();
        var repository = new Mock<IBusinessReviewRepository>();
        repository.Setup(value => value.GetOwnedBookingAsync(booking.Id, booking.UserId, default))
            .ReturnsAsync(booking);
        repository.Setup(value => value.GetOwnedReviewAsync(
                booking.Id, booking.UserId, true, default))
            .ReturnsAsync((BusinessReview?)null);
        BusinessReview? added = null;
        repository.Setup(value => value.AddAsync(It.IsAny<BusinessReview>(), default))
            .Callback<BusinessReview, CancellationToken>((value, _) =>
            {
                value.RowVersion = [1, 2, 3];
                added = value;
            })
            .Returns(Task.CompletedTask);
        repository.Setup(value => value.SaveAsync(default)).Returns(Task.CompletedTask);
        var service = CreateService(repository);

        var result = await service.PutAsync(
            booking.Id,
            booking.UserId,
            new PutBusinessReviewRequest
            {
                Rating = 5,
                Comment = "  Excellent service.  "
            },
            default);

        result.Created.Should().BeTrue();
        result.Review.Comment.Should().Be("Excellent service.");
        result.Review.BusinessId.Should().Be(booking.BusinessSourceId);
        result.Review.RowVersion.Should().Be(Convert.ToBase64String([1, 2, 3]));
        added.Should().NotBeNull();
    }

    [Fact]
    public async Task Put_normalizes_blank_comment_to_null()
    {
        var booking = CompletedBooking();
        var repository = new Mock<IBusinessReviewRepository>();
        repository.Setup(value => value.GetOwnedBookingAsync(booking.Id, booking.UserId, default))
            .ReturnsAsync(booking);
        repository.Setup(value => value.GetOwnedReviewAsync(
                booking.Id, booking.UserId, true, default))
            .ReturnsAsync((BusinessReview?)null);
        BusinessReview? added = null;
        repository.Setup(value => value.AddAsync(It.IsAny<BusinessReview>(), default))
            .Callback<BusinessReview, CancellationToken>((value, _) =>
            {
                value.RowVersion = [1];
                added = value;
            })
            .Returns(Task.CompletedTask);
        repository.Setup(value => value.SaveAsync(default)).Returns(Task.CompletedTask);

        await CreateService(repository).PutAsync(
            booking.Id,
            booking.UserId,
            new PutBusinessReviewRequest { Rating = 5, Comment = "   " },
            default);

        added!.Comment.Should().BeNull();
    }

    [Theory]
    [InlineData(BookingStatuses.Pending)]
    [InlineData(BookingStatuses.Confirmed)]
    [InlineData(BookingStatuses.InProgress)]
    [InlineData(BookingStatuses.Cancelled)]
    [InlineData(BookingStatuses.NoShow)]
    public async Task Put_rejects_every_non_completed_booking_state(string status)
    {
        var booking = CompletedBooking();
        booking.Status = status;
        var repository = new Mock<IBusinessReviewRepository>();
        repository.Setup(value => value.GetOwnedBookingAsync(booking.Id, booking.UserId, default))
            .ReturnsAsync(booking);

        var action = () => CreateService(repository).PutAsync(
            booking.Id,
            booking.UserId,
            new PutBusinessReviewRequest { Rating = 5 },
            default);

        (await action.Should().ThrowAsync<BusinessReviewException>())
            .Which.Code.Should().Be(BusinessReviewProblemCodes.BookingNotCompleted);
    }

    [Fact]
    public async Task Get_returns_non_disclosing_booking_not_found_for_unowned_booking()
    {
        var repository = new Mock<IBusinessReviewRepository>();
        repository.Setup(value => value.GetOwnedBookingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), default))
            .ReturnsAsync((CustomerBooking?)null);

        var action = () => CreateService(repository).GetOwnedAsync(
            Guid.NewGuid(), Guid.NewGuid(), default);

        var exception = (await action.Should().ThrowAsync<BusinessReviewException>()).Which;
        exception.StatusCode.Should().Be(404);
        exception.Code.Should().Be(BusinessReviewProblemCodes.BookingNotFound);
    }

    [Fact]
    public async Task Put_requires_current_rowversion_for_update()
    {
        var booking = CompletedBooking();
        var review = Review(booking, [9, 8, 7]);
        var repository = new Mock<IBusinessReviewRepository>();
        repository.Setup(value => value.GetOwnedBookingAsync(booking.Id, booking.UserId, default))
            .ReturnsAsync(booking);
        repository.Setup(value => value.GetOwnedReviewAsync(
                booking.Id, booking.UserId, true, default))
            .ReturnsAsync(review);

        var action = () => CreateService(repository).PutAsync(
            booking.Id,
            booking.UserId,
            new PutBusinessReviewRequest
            {
                Rating = 4,
                ExpectedRowVersion = Convert.ToBase64String([1])
            },
            default);

        (await action.Should().ThrowAsync<BusinessReviewException>())
            .Which.Code.Should().Be(BusinessReviewProblemCodes.VersionConflict);
        review.Rating.Should().Be(5);
    }

    [Fact]
    public async Task Put_maps_concurrent_create_unique_violation_to_version_conflict()
    {
        var booking = CompletedBooking();
        var repository = new Mock<IBusinessReviewRepository>();
        repository.Setup(value => value.GetOwnedBookingAsync(booking.Id, booking.UserId, default))
            .ReturnsAsync(booking);
        repository.Setup(value => value.GetOwnedReviewAsync(
                booking.Id, booking.UserId, true, default))
            .ReturnsAsync((BusinessReview?)null);
        repository.Setup(value => value.SaveAsync(default))
            .ThrowsAsync(new BusinessReviewDuplicateException());

        var action = () => CreateService(repository).PutAsync(
            booking.Id, booking.UserId,
            new PutBusinessReviewRequest { Rating = 5 }, default);

        (await action.Should().ThrowAsync<BusinessReviewException>())
            .Which.Code.Should().Be(BusinessReviewProblemCodes.VersionConflict);
    }

    [Fact]
    public async Task Public_list_rounds_average_masks_name_and_omits_private_fields_by_contract()
    {
        var catalogId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var booking = CompletedBooking();
        var first = Review(booking, [1]);
        first.Rating = 4;
        first.User = new User { FullName = "Maya Demo" };
        var second = Review(booking, [2]);
        second.Rating = 5;
        second.User = new User { FullName = "Omar Demo" };
        var repository = new Mock<IBusinessReviewRepository>();
        repository.Setup(value => value.ResolveBusinessSourceIdAsync(catalogId, default))
            .ReturnsAsync(sourceId);
        repository.Setup(value => value.GetPublicPageAsync(sourceId, 1, 20, default))
            .ReturnsAsync((
                (IReadOnlyList<BusinessReview>)[first, second],
                2,
                4.45m));

        var response = await CreateService(repository).GetPublicAsync(
            catalogId, 1, 20, default);

        response.AverageRating.Should().Be(4.5m);
        response.RatingCount.Should().Be(2);
        response.Items.Select(value => value.CustomerDisplayName)
            .Should().Equal("M***", "O***");
        typeof(PublicBusinessReviewResponse).GetProperties().Select(value => value.Name)
            .Should().BeEquivalentTo(
                "Id", "Rating", "Comment", "CustomerDisplayName", "CreatedAtUtc");
    }

    [Fact]
    public async Task Public_list_uses_zero_average_when_no_reviews_exist()
    {
        var catalogId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var repository = new Mock<IBusinessReviewRepository>();
        repository.Setup(value => value.ResolveBusinessSourceIdAsync(catalogId, default))
            .ReturnsAsync(sourceId);
        repository.Setup(value => value.GetPublicPageAsync(sourceId, 1, 20, default))
            .ReturnsAsync((Array.Empty<BusinessReview>(), 0, 0m));

        var response = await CreateService(repository).GetPublicAsync(
            catalogId, 1, 20, default);

        response.AverageRating.Should().Be(0m);
        response.RatingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null, "***")]
    [InlineData("", "***")]
    [InlineData("   ", "***")]
    [InlineData("A", "A***")]
    [InlineData("علي", "ع***")]
    [InlineData("דוד", "ד***")]
    public async Task Public_list_masks_every_display_name_boundary(
        string? fullName,
        string expected)
    {
        var catalogId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var booking = CompletedBooking();
        var review = Review(booking, [1]);
        review.User = new User { FullName = fullName! };
        var repository = new Mock<IBusinessReviewRepository>();
        repository.Setup(value => value.ResolveBusinessSourceIdAsync(catalogId, default))
            .ReturnsAsync(sourceId);
        repository.Setup(value => value.GetPublicPageAsync(sourceId, 1, 20, default))
            .ReturnsAsync(((IReadOnlyList<BusinessReview>)[review], 1, 5m));

        var response = await CreateService(repository).GetPublicAsync(
            catalogId, 1, 20, default);

        response.Items.Should().ContainSingle()
            .Which.CustomerDisplayName.Should().Be(expected);
    }

    private static BusinessReviewService CreateService(
        Mock<IBusinessReviewRepository> repository) =>
        new(repository.Object, new FixedTimeProvider(Now));

    private static CustomerBooking CompletedBooking() => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        BusinessSourceId = Guid.NewGuid(),
        Status = BookingStatuses.Completed
    };

    private static BusinessReview Review(CustomerBooking booking, byte[] rowVersion) => new()
    {
        Id = Guid.NewGuid(),
        CustomerBookingId = booking.Id,
        UserId = booking.UserId,
        BusinessSourceId = booking.BusinessSourceId,
        Rating = 5,
        CreatedAtUtc = Now.AddDays(-1),
        UpdatedAtUtc = Now,
        RowVersion = rowVersion
    };

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
