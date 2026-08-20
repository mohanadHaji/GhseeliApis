using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Repositories;

public abstract class BusinessMutationRepositoryBase
{
    private const int MaximumCompanyOnlyConflictRetries = 2;

    protected BusinessMutationRepositoryBase(BusinessDbContext context)
    {
        Context = context;
    }

    protected BusinessDbContext Context { get; }

    protected async Task PrepareCompanyVersionIncrementAsync(Guid companyId)
    {
        var company = await GetTrackedCompanyAsync(companyId);
        company.CatalogVersion = checked(company.CatalogVersion + 1);
        company.UpdatedAt = DateTime.UtcNow;
    }

    protected async Task PersistMutationAsync(
        Guid companyId,
        string conflictMessage,
        bool allowCompanyOnlyRetry,
        bool allowCompanyProfileRetry = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await Context.SaveChangesAsync();
                return;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                if (allowCompanyOnlyRetry &&
                    attempt < MaximumCompanyOnlyConflictRetries &&
                    await TryRefreshCompanyAfterConflictAsync(companyId, exception, allowCompanyProfileRetry))
                {
                    continue;
                }

                throw new BusinessConflictException(conflictMessage, innerException: exception);
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                throw new BusinessConflictException(conflictMessage, innerException: exception);
            }
        }
    }

    private async Task<Company> GetTrackedCompanyAsync(Guid companyId)
    {
        var trackedCompany = Context.Companies.Local.SingleOrDefault(company => company.Id == companyId);
        if (trackedCompany is not null)
        {
            return trackedCompany;
        }

        return await Context.Companies.SingleAsync(company => company.Id == companyId);
    }

    private async Task<bool> TryRefreshCompanyAfterConflictAsync(
        Guid companyId,
        DbUpdateConcurrencyException exception,
        bool allowCompanyProfileRetry)
    {
        var companyEntries = exception.Entries
            .Where(entry => entry.Entity is Company)
            .ToArray();

        if (companyEntries.Length != exception.Entries.Count || companyEntries.Length != 1)
        {
            return false;
        }

        var companyEntry = companyEntries[0];
        if (((Company)companyEntry.Entity).Id != companyId)
        {
            return false;
        }

        var databaseValues = await companyEntry.GetDatabaseValuesAsync();
        if (databaseValues is null)
        {
            return false;
        }

        if (allowCompanyProfileRetry && CompanyDataChangedBeyondVersion(companyEntry.OriginalValues, databaseValues))
        {
            return false;
        }

        var nextCatalogVersion = checked(databaseValues.GetValue<long>(nameof(Company.CatalogVersion)) + 1);
        companyEntry.OriginalValues.SetValues(databaseValues);
        companyEntry.CurrentValues[nameof(Company.CatalogVersion)] = nextCatalogVersion;
        companyEntry.CurrentValues[nameof(Company.UpdatedAt)] = DateTime.UtcNow;
        return true;
    }

    private static bool CompanyDataChangedBeyondVersion(
        PropertyValues originalValues,
        PropertyValues databaseValues)
    {
        foreach (var property in originalValues.Properties)
        {
            if (property.Name is nameof(Company.CatalogVersion) or nameof(Company.UpdatedAt) or nameof(Company.RowVersion))
            {
                continue;
            }

            var originalValue = originalValues[property];
            var databaseValue = databaseValues[property];
            if (!Equals(originalValue, databaseValue))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException sqlException &&
            (sqlException.Number == 2601 || sqlException.Number == 2627);
    }
}
