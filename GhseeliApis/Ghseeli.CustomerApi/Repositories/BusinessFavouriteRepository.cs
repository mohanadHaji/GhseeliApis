using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

public sealed class BusinessFavouriteRepository : IBusinessFavouriteRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessFavouriteRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlySet<Guid>> GetBusinessSourceIdsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> businessSourceIds,
        CancellationToken cancellationToken)
    {
        if (businessSourceIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        return (await _context.BusinessFavourites
                .AsNoTracking()
                .Where(favourite =>
                    favourite.UserId == userId &&
                    businessSourceIds.Contains(favourite.BusinessSourceId))
                .Select(favourite => favourite.BusinessSourceId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }

    public async Task AddIfMissingAsync(
        Guid userId,
        Guid businessSourceId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        if (await _context.BusinessFavourites.AnyAsync(
                favourite =>
                    favourite.UserId == userId &&
                    favourite.BusinessSourceId == businessSourceId,
                cancellationToken))
        {
            return;
        }

        var favourite = new BusinessFavourite
        {
            UserId = userId,
            BusinessSourceId = businessSourceId,
            CreatedAtUtc = createdAtUtc
        };
        _context.BusinessFavourites.Add(favourite);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueConstraintViolation(exception))
        {
            _context.Entry(favourite).State = EntityState.Detached;
        }
    }

    public async Task DeleteIfPresentAsync(
        Guid userId,
        Guid businessSourceId,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            await _context.BusinessFavourites
                .Where(value =>
                    value.UserId == userId &&
                    value.BusinessSourceId == businessSourceId)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var favourite = await _context.BusinessFavourites.SingleOrDefaultAsync(
            value =>
                value.UserId == userId &&
                value.BusinessSourceId == businessSourceId,
            cancellationToken);
        if (favourite is null)
        {
            return;
        }

        _context.BusinessFavourites.Remove(favourite);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
            when (exception.Entries.Count > 0 &&
                  exception.Entries.All(entry =>
                      entry.Entity is BusinessFavourite &&
                      entry.State == EntityState.Deleted))
        {
            foreach (var entry in exception.Entries)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }
}
