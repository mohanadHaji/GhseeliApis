using GhseeliApis.Models;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Persistence;

internal static class BusinessReviewConfiguration
{
    public static void ConfigureBusinessReviews(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BusinessReview>(entity =>
        {
            entity.ToTable("BusinessReviews", table =>
                table.HasCheckConstraint(
                    "CK_BusinessReviews_Rating",
                    "[Rating] >= 1 AND [Rating] <= 5"));
            entity.HasKey(review => review.Id);
            entity.Property(review => review.Comment).HasMaxLength(1000);
            entity.Property(review => review.CreatedAtUtc).IsRequired();
            entity.Property(review => review.UpdatedAtUtc).IsRequired();
            entity.Property(review => review.IsDemo).HasDefaultValue(false).IsRequired();
            entity.Property(review => review.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.HasOne(review => review.User)
                .WithMany(user => user.BusinessReviews)
                .HasForeignKey(review => review.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(review => review.CustomerBooking)
                .WithOne(booking => booking.Review)
                .HasForeignKey<BusinessReview>(review => review.CustomerBookingId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(review => new { review.UserId, review.CreatedAtUtc });
            entity.HasIndex(review => new
            {
                review.BusinessSourceId,
                review.IsDemo,
                review.CreatedAtUtc,
                review.Id
            });
        });
    }
}
