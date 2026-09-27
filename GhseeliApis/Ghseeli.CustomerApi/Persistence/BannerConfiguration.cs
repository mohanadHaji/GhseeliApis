using GhseeliApis.Models;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Persistence;

internal static class BannerConfiguration
{
    public static void ConfigureBanners(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Banner>(entity =>
        {
            entity.ToTable("Banners", table =>
                table.HasCheckConstraint(
                    "CK_Banners_DisplayOrder",
                    "[DisplayOrder] >= 0 AND [DisplayOrder] <= 10000"));
            entity.HasKey(banner => banner.Id);
            entity.Property(banner => banner.ImageUrl)
                .HasMaxLength(500)
                .IsRequired();
            entity.Property(banner => banner.DisplayOrder).IsRequired();
            entity.Property(banner => banner.IsActive).IsRequired();
            entity.Property(banner => banner.CreatedAtUtc).IsRequired();
            entity.Property(banner => banner.UpdatedAtUtc).IsRequired();
            entity.Property(banner => banner.IsDemo)
                .HasDefaultValue(false)
                .IsRequired();
            entity.Property(banner => banner.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.HasIndex(banner => new
            {
                banner.IsDemo,
                banner.IsActive,
                banner.DisplayOrder,
                banner.Id
            });
        });
    }
}
