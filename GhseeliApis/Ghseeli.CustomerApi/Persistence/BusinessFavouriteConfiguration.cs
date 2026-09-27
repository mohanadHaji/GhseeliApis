using GhseeliApis.Models;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Persistence;

internal static class BusinessFavouriteConfiguration
{
    public static void ConfigureBusinessFavourites(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BusinessFavourite>(entity =>
        {
            entity.ToTable("BusinessFavourites");
            entity.HasKey(favourite => favourite.Id);
            entity.Property(favourite => favourite.CreatedAtUtc).IsRequired();
            entity.Property(favourite => favourite.IsDemo).HasDefaultValue(false).IsRequired();
            entity.HasOne(favourite => favourite.User)
                .WithMany(user => user.BusinessFavourites)
                .HasForeignKey(favourite => favourite.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(favourite => new
            {
                favourite.UserId,
                favourite.BusinessSourceId,
                favourite.IsDemo
            }).IsUnique();
            entity.HasIndex(favourite => new
            {
                favourite.BusinessSourceId,
                favourite.IsDemo
            });
        });
    }
}
