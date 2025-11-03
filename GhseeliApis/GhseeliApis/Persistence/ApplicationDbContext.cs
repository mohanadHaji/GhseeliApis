using GhseeliApis.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Persistence;

/// <summary>
/// Application database context for car washing service platform
/// </summary>
public class ApplicationDbContext : IdentityDbContext<Models.User, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // DbSets for all entities
    public DbSet<UserAddress> UserAddresses => Set<UserAddress>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyAvailability> CompanyAvailabilities => Set<CompanyAvailability>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServiceOption> ServiceOptions => Set<ServiceOption>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // IMPORTANT: Call base.OnModelCreating() first to configure Identity tables
        base.OnModelCreating(modelBuilder);

        // ============================================
        // User Configuration (extends IdentityUser)
        // ============================================
        modelBuilder.Entity<Models.User>(entity =>
        {
            // Custom property configurations
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .IsRequired();
            
            entity.Property(e => e.UpdatedAt)
                .IsRequired(false);

            entity.Property(e => e.FullName)
                .HasMaxLength(150)
                .IsRequired();
            
            entity.Property(e => e.Email)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.Phone)
                .HasMaxLength(30);

            // Unique index on email
            entity.HasIndex(e => e.Email)
                .IsUnique();
        });

        // ============================================
        // User and Wallet 1:1 Relationship
        // ============================================
        modelBuilder.Entity<Models.User>()
            .HasOne(u => u.Wallet)
            .WithOne(w => w.User)
            .HasForeignKey<Wallet>(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================
        // User -> Addresses Relationship
        // ============================================
        modelBuilder.Entity<UserAddress>()
            .HasOne(a => a.User)
            .WithMany(u => u.Addresses)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================
        // User -> Vehicles Relationship
        // ============================================
        modelBuilder.Entity<Vehicle>()
            .HasOne(v => v.Owner)
            .WithMany(u => u.Vehicles)
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index for vehicle lookup
        modelBuilder.Entity<Vehicle>()
            .HasIndex(v => new { v.UserId, v.LicensePlate });

        // ============================================
        // Company Availabilities Relationship
        // ============================================
        modelBuilder.Entity<CompanyAvailability>()
            .HasOne(ca => ca.Company)
            .WithMany(c => c.Availabilities)
            .HasForeignKey(ca => ca.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================
        // Service -> ServiceOption Relationship
        // ============================================
        modelBuilder.Entity<ServiceOption>()
            .HasOne(so => so.Service)
            .WithMany(s => s.Options)
            .HasForeignKey(so => so.ServiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================
        // Company -> ServiceOption Relationship (Optional)
        // ============================================
        modelBuilder.Entity<ServiceOption>()
            .HasOne(so => so.Company)
            .WithMany(c => c.ServiceOptions)
            .HasForeignKey(so => so.CompanyId)
            .OnDelete(DeleteBehavior.SetNull);

        // Index for service option lookup
        modelBuilder.Entity<ServiceOption>()
            .HasIndex(so => new { so.ServiceId, so.CompanyId, so.Name });

        // ============================================
        // Booking Relationships
        // ============================================
        modelBuilder.Entity<Booking>()
            .HasOne(b => b.User)
            .WithMany(u => u.Bookings)
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Company)
            .WithMany(c => c.Bookings)
            .HasForeignKey(b => b.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.ServiceOption)
            .WithMany(so => so.Bookings)
            .HasForeignKey(b => b.ServiceOptionId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Vehicle)
            .WithMany(v => v.Bookings)
            .HasForeignKey(b => b.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Address)
            .WithMany(a => a.Bookings)
            .HasForeignKey(b => b.AddressId)
            .OnDelete(DeleteBehavior.Restrict);

        // ============================================
        // Booking <-> Payment 1:1 Relationship
        // ============================================
        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Booking)
            .WithOne(b => b.Payment)
            .HasForeignKey<Payment>(p => p.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Payment>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ============================================
        // WalletTransaction Relationships
        // ============================================
        modelBuilder.Entity<WalletTransaction>()
            .HasOne(t => t.User)
            .WithMany(u => u.WalletTransactions)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // WalletTransaction optional Booking
        modelBuilder.Entity<WalletTransaction>()
            .HasOne(t => t.Booking)
            .WithMany()
            .HasForeignKey(t => t.BookingId)
            .OnDelete(DeleteBehavior.SetNull);

        // ============================================
        // Notifications Relationship
        // ============================================
        modelBuilder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany(u => u.Notifications)
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================
        // Decimal Precision Configurations
        // ============================================
        modelBuilder.Entity<ServiceOption>()
            .Property(so => so.Price)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Payment>()
            .Property(p => p.Amount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Wallet>()
            .Property(w => w.Balance)
            .HasPrecision(18, 2);

        modelBuilder.Entity<WalletTransaction>()
            .Property(wt => wt.Amount)
            .HasPrecision(18, 2);
    }
}
