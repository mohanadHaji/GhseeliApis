using GhseeliApis.DTOs.Banners;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Services.Banners;

public interface IBannerService
{
    Task<PublicBannersResponse> GetPublicAsync(CancellationToken cancellationToken);
    Task<AdminBannersResponse> GetAdminAsync(CancellationToken cancellationToken);
    Task<AdminBannerResponse> CreateAsync(
        CreateBannerRequest request, CancellationToken cancellationToken);
    Task<AdminBannerResponse> UpdateAsync(
        Guid id, UpdateBannerRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(
        Guid id, string expectedRowVersion, CancellationToken cancellationToken);
}

public sealed class BannerException : Exception
{
    public BannerException(int statusCode, string code) : base(code)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}

public sealed class BannerService : IBannerService
{
    private readonly IBannerRepository _repository;
    private readonly TimeProvider _timeProvider;

    public BannerService(IBannerRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<PublicBannersResponse> GetPublicAsync(
        CancellationToken cancellationToken) =>
        new()
        {
            Banners = (await _repository.GetPublicAsync(cancellationToken))
                .Select(MapPublic)
                .ToArray()
        };

    public async Task<AdminBannersResponse> GetAdminAsync(
        CancellationToken cancellationToken) =>
        new()
        {
            Banners = (await _repository.GetAdminAsync(cancellationToken))
                .Select(MapAdmin)
                .ToArray()
        };

    public async Task<AdminBannerResponse> CreateAsync(
        CreateBannerRequest request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var banner = new Banner
        {
            ImageUrl = request.ImageUrl.Trim(),
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        await _repository.AddAsync(banner, cancellationToken);
        await _repository.SaveAsync(cancellationToken);
        return MapAdmin(banner);
    }

    public async Task<AdminBannerResponse> UpdateAsync(
        Guid id,
        UpdateBannerRequest request,
        CancellationToken cancellationToken)
    {
        var banner = await _repository.GetTrackedAsync(id, cancellationToken)
            ?? throw new BannerException(404, BannerProblemCodes.NotFound);
        RequireVersion(banner, request.ExpectedRowVersion);
        banner.ImageUrl = request.ImageUrl.Trim();
        banner.DisplayOrder = request.DisplayOrder;
        banner.IsActive = request.IsActive;
        banner.UpdatedAtUtc = _timeProvider.GetUtcNow();
        await SaveWithConcurrencyAsync(cancellationToken);
        return MapAdmin(banner);
    }

    public async Task DeleteAsync(
        Guid id,
        string expectedRowVersion,
        CancellationToken cancellationToken)
    {
        var banner = await _repository.GetTrackedAsync(id, cancellationToken);
        if (banner is null)
        {
            throw new BannerException(409, BannerProblemCodes.VersionConflict);
        }

        RequireVersion(banner, expectedRowVersion);
        _repository.Delete(banner);
        await SaveWithConcurrencyAsync(cancellationToken);
    }

    private async Task SaveWithConcurrencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _repository.SaveAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BannerException(409, BannerProblemCodes.VersionConflict);
        }
    }

    private static void RequireVersion(Banner banner, string expected)
    {
        if (!BannerRowVersion.IsValid(expected) ||
            !banner.RowVersion.SequenceEqual(Convert.FromBase64String(expected)))
        {
            throw new BannerException(409, BannerProblemCodes.VersionConflict);
        }
    }

    private static PublicBannerResponse MapPublic(Banner banner) => new()
    {
        Id = banner.Id,
        ImageUrl = banner.ImageUrl,
        DisplayOrder = banner.DisplayOrder
    };

    private static AdminBannerResponse MapAdmin(Banner banner) => new()
    {
        Id = banner.Id,
        ImageUrl = banner.ImageUrl,
        DisplayOrder = banner.DisplayOrder,
        IsActive = banner.IsActive,
        CreatedAtUtc = banner.CreatedAtUtc,
        UpdatedAtUtc = banner.UpdatedAtUtc,
        RowVersion = Convert.ToBase64String(banner.RowVersion)
    };
}
