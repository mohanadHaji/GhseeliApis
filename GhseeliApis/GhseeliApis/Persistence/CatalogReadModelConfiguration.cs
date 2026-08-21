using GhseeliApis.Models;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Persistence;

internal static class CatalogReadModelConfiguration
{
    public static void ConfigureCatalogReadModel(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogProviderReadModel>(entity =>
        {
            entity.ToTable("CatalogProviders");
            entity.HasKey(provider => provider.Id);
            entity.Property(provider => provider.NameAr)
                .HasMaxLength(200);
            entity.Property(provider => provider.NameHe)
                .HasMaxLength(200);
            entity.Property(provider => provider.DescriptionAr)
                .HasMaxLength(1000);
            entity.Property(provider => provider.DescriptionHe)
                .HasMaxLength(1000);
            entity.Property(provider => provider.Phone)
                .HasMaxLength(30);
            entity.Property(provider => provider.SnapshotHash)
                .HasMaxLength(64);
            entity.Property(provider => provider.LastFailureCode)
                .HasMaxLength(100);
            entity.Property(provider => provider.RefreshLeaseToken)
                .HasMaxLength(64);
            entity.Property(provider => provider.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.HasIndex(provider => provider.SourceCompanyId)
                .IsUnique();
            entity.HasIndex(provider => new
            {
                provider.IsEnabled,
                provider.DisplayOrder
            });
        });

        modelBuilder.Entity<CatalogBranchReadModel>(entity =>
        {
            entity.ToTable("CatalogBranches");
            entity.HasKey(branch => branch.Id);
            entity.Property(branch => branch.NameAr)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(branch => branch.NameHe)
                .HasMaxLength(200);
            entity.Property(branch => branch.AddressAr)
                .HasMaxLength(300)
                .IsRequired();
            entity.Property(branch => branch.AddressHe)
                .HasMaxLength(300);
            entity.HasOne(branch => branch.Provider)
                .WithMany(provider => provider.Branches)
                .HasForeignKey(branch => branch.ProviderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(branch => branch.SourceBranchId)
                .IsUnique();
            entity.HasIndex(branch => new
            {
                branch.ProviderId,
                branch.DisplayOrder
            });
        });

        modelBuilder.Entity<CatalogCategoryReadModel>(entity =>
        {
            entity.ToTable("CatalogCategories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.NameAr)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(category => category.NameHe)
                .HasMaxLength(200);
            entity.Property(category => category.DescriptionAr)
                .HasMaxLength(1000);
            entity.Property(category => category.DescriptionHe)
                .HasMaxLength(1000);
            entity.HasOne(category => category.Provider)
                .WithMany(provider => provider.Categories)
                .HasForeignKey(category => category.ProviderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(category => category.SourceCategoryId)
                .IsUnique();
            entity.HasIndex(category => new
            {
                category.ProviderId,
                category.DisplayOrder
            });
        });

        modelBuilder.Entity<CatalogOfferingReadModel>(entity =>
        {
            entity.ToTable("CatalogOfferings");
            entity.HasKey(offering => offering.Id);
            entity.Property(offering => offering.NameAr)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(offering => offering.NameHe)
                .HasMaxLength(200);
            entity.Property(offering => offering.DescriptionAr)
                .HasMaxLength(1000);
            entity.Property(offering => offering.DescriptionHe)
                .HasMaxLength(1000);
            entity.Property(offering => offering.ImageUrl)
                .HasMaxLength(500);
            entity.Property(offering => offering.ReferenceCode)
                .HasMaxLength(100);
            entity.Property(offering => offering.BasePrice)
                .HasPrecision(18, 2);
            entity.HasOne(offering => offering.Category)
                .WithMany(category => category.Offerings)
                .HasForeignKey(offering => offering.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(offering => offering.Branch)
                .WithMany(branch => branch.Offerings)
                .HasForeignKey(offering => offering.BranchId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(offering => offering.SourceOfferingId)
                .IsUnique();
            entity.HasIndex(offering => new
            {
                offering.CategoryId,
                offering.DisplayOrder
            });
            entity.HasIndex(offering => new
            {
                offering.BranchId,
                offering.DisplayOrder
            });
        });

        modelBuilder.Entity<CatalogAddonGroupReadModel>(entity =>
        {
            entity.ToTable("CatalogAddonGroups");
            entity.HasKey(addonGroup => addonGroup.Id);
            entity.Property(addonGroup => addonGroup.NameAr)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(addonGroup => addonGroup.NameHe)
                .HasMaxLength(200);
            entity.Property(addonGroup => addonGroup.DescriptionAr)
                .HasMaxLength(1000);
            entity.Property(addonGroup => addonGroup.DescriptionHe)
                .HasMaxLength(1000);
            entity.Property(addonGroup => addonGroup.SelectionType)
                .HasMaxLength(50)
                .IsRequired();
            entity.HasOne(addonGroup => addonGroup.Offering)
                .WithMany(offering => offering.AddonGroups)
                .HasForeignKey(addonGroup => addonGroup.OfferingId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(addonGroup => addonGroup.SourceAddonGroupId)
                .IsUnique();
            entity.HasIndex(addonGroup => new
            {
                addonGroup.OfferingId,
                addonGroup.DisplayOrder
            });
        });

        modelBuilder.Entity<CatalogAddonChoiceReadModel>(entity =>
        {
            entity.ToTable("CatalogAddonChoices");
            entity.HasKey(addonChoice => addonChoice.Id);
            entity.Property(addonChoice => addonChoice.NameAr)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(addonChoice => addonChoice.NameHe)
                .HasMaxLength(200);
            entity.Property(addonChoice => addonChoice.DescriptionAr)
                .HasMaxLength(1000);
            entity.Property(addonChoice => addonChoice.DescriptionHe)
                .HasMaxLength(1000);
            entity.Property(addonChoice => addonChoice.PriceAdjustment)
                .HasPrecision(18, 2);
            entity.HasOne(addonChoice => addonChoice.AddonGroup)
                .WithMany(addonGroup => addonGroup.Choices)
                .HasForeignKey(addonChoice => addonChoice.AddonGroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(addonChoice => addonChoice.SourceAddonChoiceId)
                .IsUnique();
            entity.HasIndex(addonChoice => new
            {
                addonChoice.AddonGroupId,
                addonChoice.DisplayOrder
            });
        });
    }
}
