using GhseeliApis.DTOs.Reviews;
using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

public interface IBusinessReviewRepository
{
    Task<CustomerBooking?> GetOwnedBookingAsync(
        Guid bookingId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<BusinessReview?> GetOwnedReviewAsync(
        Guid bookingId,
        Guid userId,
        bool tracking,
        CancellationToken cancellationToken);

    Task AddAsync(BusinessReview review, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    void Delete(BusinessReview review);

    Task<Guid?> ResolveBusinessSourceIdAsync(
        Guid catalogBusinessId,
        CancellationToken cancellationToken);

    Task<(IReadOnlyList<BusinessReview> Items, int TotalCount, decimal AverageRating)>
        GetPublicPageAsync(
            Guid businessSourceId,
            int page,
            int pageSize,
            CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, BusinessRatingAggregate>> GetAggregatesAsync(
        IReadOnlyCollection<Guid> businessSourceIds,
        CancellationToken cancellationToken);
}

public sealed class BusinessReviewDuplicateException : Exception;
