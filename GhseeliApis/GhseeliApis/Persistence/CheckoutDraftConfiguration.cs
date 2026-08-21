using GhseeliApis.Models;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Persistence;

internal static class CheckoutDraftConfiguration
{
    public static void ConfigureCheckoutDrafts(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CheckoutDraft>(entity =>
        {
            entity.ToTable("CheckoutDrafts");
            entity.HasKey(draft => draft.Id);
            entity.Property(draft => draft.OrderGuid).IsRequired();
            entity.Property(draft => draft.OwnerDeviceId).IsRequired();
            entity.Property(draft => draft.BusinessSourceId).IsRequired();
            entity.Property(draft => draft.BranchSourceId).IsRequired();
            entity.Property(draft => draft.CatalogVersion).IsRequired();
            entity.Property(draft => draft.PublicVersion).IsRequired();
            entity.Property(draft => draft.RequestedSlotStartUtc).IsRequired();
            entity.Property(draft => draft.VehicleType)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(draft => draft.LicensePlate)
                .HasMaxLength(50);
            entity.Property(draft => draft.VehicleMake)
                .HasMaxLength(150);
            entity.Property(draft => draft.VehicleModel)
                .HasMaxLength(150);
            entity.Property(draft => draft.VehicleColor)
                .HasMaxLength(50);
            entity.Property(draft => draft.AddressLine)
                .HasMaxLength(300)
                .IsRequired();
            entity.Property(draft => draft.City)
                .HasMaxLength(120);
            entity.Property(draft => draft.Area)
                .HasMaxLength(120);
            entity.Property(draft => draft.Latitude)
                .HasPrecision(9, 6);
            entity.Property(draft => draft.Longitude)
                .HasPrecision(9, 6);
            entity.Property(draft => draft.RequiresReprice).IsRequired();
            entity.Property(draft => draft.CreatedAt).IsRequired();
            entity.Property(draft => draft.UpdatedAt).IsRequired();
            entity.Property(draft => draft.ExpiresAt).IsRequired();
            entity.Property(draft => draft.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.HasIndex(draft => draft.OrderGuid).IsUnique();
            entity.HasIndex(draft => new
            {
                draft.OwnerDeviceId,
                draft.OrderGuid
            });
            entity.HasIndex(draft => new
            {
                draft.OwnerDeviceId,
                draft.ExpiresAt
            });
        });

        modelBuilder.Entity<CheckoutDraftItem>(entity =>
        {
            entity.ToTable("CheckoutDraftItems");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.OfferingSourceId).IsRequired();
            entity.Property(item => item.DisplayOrder).IsRequired();
            entity.HasOne(item => item.CheckoutDraft)
                .WithMany(draft => draft.Items)
                .HasForeignKey(item => item.CheckoutDraftId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new
            {
                item.CheckoutDraftId,
                item.DisplayOrder
            });
            entity.HasIndex(item => new
            {
                item.CheckoutDraftId,
                item.OfferingSourceId
            }).IsUnique();
        });

        modelBuilder.Entity<CheckoutDraftSelection>(entity =>
        {
            entity.ToTable("CheckoutDraftSelections");
            entity.HasKey(selection => selection.Id);
            entity.Property(selection => selection.AddonGroupSourceId).IsRequired();
            entity.Property(selection => selection.AddonChoiceSourceId).IsRequired();
            entity.Property(selection => selection.Quantity).IsRequired();
            entity.Property(selection => selection.DisplayOrder).IsRequired();
            entity.HasOne(selection => selection.CheckoutDraftItem)
                .WithMany(item => item.Selections)
                .HasForeignKey(selection => selection.CheckoutDraftItemId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(selection => new
            {
                selection.CheckoutDraftItemId,
                selection.DisplayOrder
            });
            entity.HasIndex(selection => new
            {
                selection.CheckoutDraftItemId,
                selection.AddonChoiceSourceId
            }).IsUnique();
        });
    }
}
