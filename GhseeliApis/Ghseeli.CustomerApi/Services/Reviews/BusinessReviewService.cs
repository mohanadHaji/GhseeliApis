using Ghseeli.IntegrationContracts.Bookings;
using GhseeliApis.DTOs.Reviews;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Services.Reviews;

public interface IBusinessReviewService
{
    Task<OwnedBusinessReviewResponse> GetOwnedAsync(
        Guid bookingId, Guid userId, CancellationToken cancellationToken);
    Task<(OwnedBusinessReviewResponse Review, bool Created)> PutAsync(
        Guid bookingId,
        Guid userId,
        PutBusinessReviewRequest request,
        CancellationToken cancellationToken);
    Task DeleteAsync(
        Guid bookingId,
        Guid userId,
        string? expectedRowVersion,
        CancellationToken cancellationToken);
    Task<PublicBusinessReviewsResponse> GetPublicAsync(
        Guid catalogBusinessId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, BusinessRatingAggregate>> GetAggregatesAsync(
        IReadOnlyCollection<Guid> businessSourceIds,
        CancellationToken cancellationToken);
}

public sealed class BusinessReviewException : Exception
{
    public BusinessReviewException(int statusCode, string code) : base(code)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}

public sealed class BusinessReviewService : IBusinessReviewService
{
    private readonly IBusinessReviewRepository _repository;
    private readonly TimeProvider _timeProvider;

    public BusinessReviewService(
        IBusinessReviewRepository repository,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<OwnedBusinessReviewResponse> GetOwnedAsync(
        Guid bookingId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await RequireOwnedCompletedBookingAsync(bookingId, userId, cancellationToken);
        var review = await _repository.GetOwnedReviewAsync(
            bookingId, userId, false, cancellationToken);
        return review is null
            ? throw new BusinessReviewException(404, BusinessReviewProblemCodes.ReviewNotFound)
            : MapOwned(review);
    }

    public async Task<(OwnedBusinessReviewResponse Review, bool Created)> PutAsync(
        Guid bookingId,
        Guid userId,
        PutBusinessReviewRequest request,
        CancellationToken cancellationToken)
    {
        var booking = await RequireOwnedCompletedBookingAsync(
            bookingId, userId, cancellationToken);
        var review = await _repository.GetOwnedReviewAsync(
            bookingId, userId, true, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var created = review is null;

        if (created)
        {
            if (request.ExpectedRowVersion is not null)
            {
                throw new BusinessReviewException(
                    409, BusinessReviewProblemCodes.VersionConflict);
            }

            review = new BusinessReview
            {
                CustomerBookingId = booking.Id,
                UserId = userId,
                BusinessSourceId = booking.BusinessSourceId,
                Rating = request.Rating,
                Comment = NormalizeComment(request.Comment),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            await _repository.AddAsync(review, cancellationToken);
        }
        else
        {
            if (request.ExpectedRowVersion is null ||
                !review!.RowVersion.SequenceEqual(
                    Convert.FromBase64String(request.ExpectedRowVersion)))
            {
                throw new BusinessReviewException(
                    409, BusinessReviewProblemCodes.VersionConflict);
            }

            review!.Rating = request.Rating;
            review.Comment = NormalizeComment(request.Comment);
            review.UpdatedAtUtc = now;
        }

        try
        {
            await _repository.SaveAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessReviewException(
                409, BusinessReviewProblemCodes.VersionConflict);
        }
        catch (BusinessReviewDuplicateException) when (created)
        {
            throw new BusinessReviewException(
                409, BusinessReviewProblemCodes.VersionConflict);
        }

        return (MapOwned(review!), created);
    }

    public async Task DeleteAsync(
        Guid bookingId,
        Guid userId,
        string? expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await RequireOwnedCompletedBookingAsync(bookingId, userId, cancellationToken);
        var review = await _repository.GetOwnedReviewAsync(
            bookingId, userId, true, cancellationToken);
        if (review is null)
        {
            if (expectedRowVersion is not null)
            {
                throw new BusinessReviewException(
                    409, BusinessReviewProblemCodes.VersionConflict);
            }

            throw new BusinessReviewException(404, BusinessReviewProblemCodes.ReviewNotFound);
        }
        if (expectedRowVersion is null ||
            !review.RowVersion.SequenceEqual(Convert.FromBase64String(expectedRowVersion)))
        {
            throw new BusinessReviewException(
                409, BusinessReviewProblemCodes.VersionConflict);
        }

        _repository.Delete(review);
        try
        {
            await _repository.SaveAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessReviewException(
                409, BusinessReviewProblemCodes.VersionConflict);
        }
    }

    public async Task<PublicBusinessReviewsResponse> GetPublicAsync(
        Guid catalogBusinessId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var businessSourceId = await _repository.ResolveBusinessSourceIdAsync(
            catalogBusinessId, cancellationToken);
        if (!businessSourceId.HasValue)
        {
            throw new BusinessReviewException(404, BusinessReviewProblemCodes.BusinessNotFound);
        }

        var result = await _repository.GetPublicPageAsync(
            businessSourceId.Value, page, pageSize, cancellationToken);
        return new PublicBusinessReviewsResponse
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = result.TotalCount,
            RatingCount = result.TotalCount,
            AverageRating = result.TotalCount == 0
                ? 0m
                : Math.Round(result.AverageRating, 1, MidpointRounding.AwayFromZero),
            Items = result.Items.Select(review => new PublicBusinessReviewResponse
            {
                Id = review.Id,
                Rating = review.Rating,
                Comment = review.Comment,
                CustomerDisplayName = MaskDisplayName(review.User.FullName),
                CreatedAtUtc = review.CreatedAtUtc
            }).ToArray()
        };
    }

    public Task<IReadOnlyDictionary<Guid, BusinessRatingAggregate>> GetAggregatesAsync(
        IReadOnlyCollection<Guid> businessSourceIds,
        CancellationToken cancellationToken) =>
        _repository.GetAggregatesAsync(businessSourceIds, cancellationToken);

    private async Task<CustomerBooking> RequireOwnedCompletedBookingAsync(
        Guid bookingId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var booking = await _repository.GetOwnedBookingAsync(
            bookingId, userId, cancellationToken);
        if (booking is null)
        {
            throw new BusinessReviewException(404, BusinessReviewProblemCodes.BookingNotFound);
        }
        if (!string.Equals(
                booking.Status, BookingStatuses.Completed, StringComparison.Ordinal))
        {
            throw new BusinessReviewException(
                409, BusinessReviewProblemCodes.BookingNotCompleted);
        }

        return booking;
    }

    private static OwnedBusinessReviewResponse MapOwned(BusinessReview review) => new()
    {
        Id = review.Id,
        BookingId = review.CustomerBookingId,
        BusinessId = review.BusinessSourceId,
        Rating = review.Rating,
        Comment = review.Comment,
        CreatedAtUtc = review.CreatedAtUtc,
        UpdatedAtUtc = review.UpdatedAtUtc,
        RowVersion = Convert.ToBase64String(review.RowVersion)
    };

    private static string? NormalizeComment(string? comment) =>
        string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

    private static string MaskDisplayName(string? fullName)
    {
        var trimmed = fullName?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "***";
        }

        return string.Concat(trimmed[0], "***");
    }
}
