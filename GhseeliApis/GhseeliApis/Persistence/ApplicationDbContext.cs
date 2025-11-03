using GhseeliApis.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Persistence;

/// <summary>
/// Application database context for Google Cloud SQL
/// </summary>
public class ApplicationDbContext : IdentityDbContext<User, IdentityRole<int>, int>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // DbSet for Users is already provided by IdentityDbContext
    // public DbSet<User> Users { get; set; } // No longer needed

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // IMPORTANT: Call base.OnModelCreating() first to configure Identity tables
        base.OnModelCreating(modelBuilder);

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            // Primary key is already configured by IdentityUser
            
            // Custom property configurations
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .IsRequired();
            
            entity.Property(e => e.UpdatedAt)
                .IsRequired(false);
            
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .IsRequired();

            // Identity properties are already configured, but you can customize them
            entity.Property(e => e.UserName)
                .HasMaxLength(256)
                .IsRequired();
            
            entity.Property(e => e.Email)
                .HasMaxLength(256)
                .IsRequired();

            // Add index for common queries
            entity.HasIndex(e => e.Email)
                .IsUnique();
            
            entity.HasIndex(e => e.IsActive);
        });

        // Identity tables will be created with these default names:
        // - AspNetUsers (your User table)
        // - AspNetRoles
        // - AspNetUserRoles
        // - AspNetUserClaims
        // - AspNetUserLogins
        // - AspNetUserTokens
        // - AspNetRoleClaims
    }
}
