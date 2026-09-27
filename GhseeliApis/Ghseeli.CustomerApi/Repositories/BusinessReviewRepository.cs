using GhseeliApis.DTOs.Reviews;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace GhseeliApis.Repositories;

public sealed class BusinessReviewRepository : IBusinessReviewRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessReviewRepository(ApplicationDbContext context) => _context = context;

    public Task<CustomerBooking?> GetOwnedBookingAsync(
        Guid bookingId,
        Guid userId,
        CancellationToken cancellationToken) =>
        _context.CustomerBookings.SingleOrDefaultAsync(
            booking => booking.Id == bookingId && booking.UserId == userId,
            cancellationToken);

    public Task<BusinessReview?> GetOwnedReviewAsync(
        Guid bookingId,
        Guid userId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        IQueryable<BusinessReview> query = _context.BusinessReviews;
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return query.SingleOrDefaultAsync(
            review => review.CustomerBookingId == bookingId && review.UserId == userId,
            cancellationToken);
    }

    public Task AddAsync(BusinessReview review, CancellationToken cancellationToken) =>
        _context.BusinessReviews.AddAsync(review, cancellationToken).AsTask();

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new BusinessReviewDuplicateException();
        }
    }

    public void Delete(BusinessReview review) => _context.BusinessReviews.Remove(review);

    public Task<Guid?> ResolveBusinessSourceIdAsync(
        Guid catalogBusinessId,
        CancellationToken cancellationToken) =>
        _context.CatalogProviders
            .AsNoTracking()
            .Where(provider => provider.Id == catalogBusinessId && provider.IsEnabled)
            .Select(provider => (Guid?)provider.SourceCompanyId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<BusinessReview> Items, int TotalCount, decimal AverageRating)>
        GetPublicPageAsync(
            Guid businessSourceId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
    {
        var query = _context.BusinessReviews
            .AsNoTracking()
            .Where(review => review.BusinessSourceId == businessSourceId);
        var totalCount = await query.CountAsync(cancellationToken);
        var average = totalCount == 0
            ? 0m
            : await query.AverageAsync(review => (decimal)review.Rating, cancellationToken);
        var items = await query
            .Include(review => review.User)
            .OrderByDescending(review => review.CreatedAtUtc)
            .ThenBy(review => review.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, totalCount, average);
    }

    public async Task<IReadOnlyDictionary<Guid, BusinessRatingAggregate>> GetAggregatesAsync(
        IReadOnlyCollection<Guid> businessSourceIds,
        CancellationToken cancellationToken)
    {
        if (businessSourceIds.Count == 0)
        {
            return new Dictionary<Guid, BusinessRatingAggregate>();
        }

        var rows = await _context.BusinessReviews
            .AsNoTracking()
            .Where(review => businessSourceIds.Contains(review.BusinessSourceId))
            .GroupBy(review => review.BusinessSourceId)
            .Select(group => new
            {
                BusinessSourceId = group.Key,
                AverageRating = group.Average(review => (decimal)review.Rating),
                RatingCount = group.Count()
            })
            .ToListAsync(cancellationToken);

        var result = businessSourceIds
            .Distinct()
            .ToDictionary(
                value => value,
                value => new BusinessRatingAggregate(value, 0m, 0));
        foreach (var value in rows)
        {
            result[value.BusinessSourceId] = new BusinessRatingAggregate(
                value.BusinessSourceId,
                Math.Round(value.AverageRating, 1, MidpointRounding.AwayFromZero),
                value.RatingCount);
        }

        return result;
    }
}
