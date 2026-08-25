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

    public DbSet<UserAddress> UserAddresses => Set<UserAddress>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<CustomerDevice> CustomerDevices => Set<CustomerDevice>();
    public DbSet<CustomerConfiguration> CustomerConfigurations => Set<CustomerConfiguration>();
    public DbSet<CatalogProviderReadModel> CatalogProviders => Set<CatalogProviderReadModel>();
    public DbSet<CatalogBranchReadModel> CatalogBranches => Set<CatalogBranchReadModel>();
    public DbSet<CatalogCategoryReadModel> CatalogCategories => Set<CatalogCategoryReadModel>();
    public DbSet<CatalogOfferingReadModel> CatalogOfferings => Set<CatalogOfferingReadModel>();
    public DbSet<CatalogAddonGroupReadModel> CatalogAddonGroups => Set<CatalogAddonGroupReadModel>();
    public DbSet<CatalogAddonChoiceReadModel> CatalogAddonChoices => Set<CatalogAddonChoiceReadModel>();
    public DbSet<CheckoutDraft> CheckoutDrafts => Set<CheckoutDraft>();
    public DbSet<CheckoutDraftItem> CheckoutDraftItems => Set<CheckoutDraftItem>();
    public DbSet<CheckoutDraftSelection> CheckoutDraftSelections => Set<CheckoutDraftSelection>();
    public DbSet<CheckoutDraftPricingSnapshot> CheckoutDraftPricingSnapshots => Set<CheckoutDraftPricingSnapshot>();
    public DbSet<CheckoutDraftPricingItemSnapshot> CheckoutDraftPricingItemSnapshots => Set<CheckoutDraftPricingItemSnapshot>();
    public DbSet<CheckoutDraftPricingSelectionSnapshot> CheckoutDraftPricingSelectionSnapshots => Set<CheckoutDraftPricingSelectionSnapshot>();
    public DbSet<CustomerBooking> CustomerBookings => Set<CustomerBooking>();
    public DbSet<CustomerPayment> CustomerPayments => Set<CustomerPayment>();
    public DbSet<CustomerPaymentIdempotencyRecord> CustomerPaymentIdempotencyRecords =>
        Set<CustomerPaymentIdempotencyRecord>();
    public DbSet<StripeWebhookEventRecord> StripeWebhookEvents => Set<StripeWebhookEventRecord>();
    public DbSet<CustomerBookingItem> CustomerBookingItems => Set<CustomerBookingItem>();
    public DbSet<CustomerBookingSelection> CustomerBookingSelections => Set<CustomerBookingSelection>();
    public DbSet<BookingConfirmationAttempt> BookingConfirmationAttempts => Set<BookingConfirmationAttempt>();
    public DbSet<CustomerInternalServiceNonce> CustomerInternalServiceNonces =>
        Set<CustomerInternalServiceNonce>();
    public DbSet<CustomerInternalIdempotencyRecord> CustomerInternalIdempotencyRecords =>
        Set<CustomerInternalIdempotencyRecord>();
    public DbSet<ProcessedBookingStatusMessage> ProcessedBookingStatusMessages =>
        Set<ProcessedBookingStatusMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // IMPORTANT: Call base.OnModelCreating() first to configure Identity tables
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(CustomerSchemaOptions.OwnedDefaultSchema);

        // ============================================
        // User Configuration (extends IdentityUser)
        // ============================================
        modelBuilder.Entity<Models.User>(entity =>
        {
            // Custom property configurations
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()")
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

            entity.Property(e => e.DeleteScheduledFor)
                .IsRequired(false);

            entity.Property(e => e.PendingEmail)
                .HasMaxLength(200)
                .IsRequired(false);

            // Unique index on email
            entity.HasIndex(e => e.Email)
                .IsUnique();
        });

        modelBuilder.Entity<CustomerDevice>(entity =>
        {
            entity.HasKey(device => device.Id);
            entity.Property(device => device.InstallationId).IsRequired();
            entity.Property(device => device.Platform).HasMaxLength(16).IsRequired();
            entity.Property(device => device.AppVersion).HasMaxLength(32);
            entity.Property(device => device.TokenHash)
                .HasColumnType("binary(32)")
                .IsRequired();
            entity.Property(device => device.CreatedAt).IsRequired();
            entity.Property(device => device.UpdatedAt).IsRequired();
            entity.Property(device => device.LastSeenAt);
            entity.Property(device => device.ExpiresAt).IsRequired();
            entity.Property(device => device.IsActive)
                .HasDefaultValue(true)
                .IsRequired();
            entity.Property(device => device.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.HasIndex(device => device.InstallationId).IsUnique();
            entity.HasIndex(device => device.TokenHash).IsUnique();
        });

        modelBuilder.Entity<CustomerConfiguration>(entity =>
        {
            entity.HasKey(configuration => configuration.Id);
            entity.Property(configuration => configuration.IsActive).IsRequired();
            entity.Property(configuration => configuration.SupportEmail)
                .HasMaxLength(254)
                .IsRequired();
            entity.Property(configuration => configuration.SupportPhone)
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(configuration => configuration.DisplayNameAr)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(configuration => configuration.DisplayNameHe)
                .HasMaxLength(200);
            entity.Property(configuration => configuration.LegalNoticeAr)
                .HasMaxLength(1000)
                .IsRequired();
            entity.Property(configuration => configuration.LegalNoticeHe)
                .HasMaxLength(1000);
            entity.Property(configuration => configuration.PrivacyPolicyUrl)
                .HasMaxLength(500)
                .IsRequired();
            entity.Property(configuration => configuration.TermsOfServiceUrl)
                .HasMaxLength(500)
                .IsRequired();
            entity.Property(configuration => configuration.MaintenanceMessageAr)
                .HasMaxLength(1000);
            entity.Property(configuration => configuration.MaintenanceMessageHe)
                .HasMaxLength(1000);
            entity.Property(configuration => configuration.CreatedAt)
                .HasDefaultValueSql("SYSDATETIMEOFFSET()")
                .IsRequired();
            entity.Property(configuration => configuration.UpdatedAt)
                .HasDefaultValueSql("SYSDATETIMEOFFSET()")
                .IsRequired();
            entity.HasIndex(configuration => configuration.IsActive)
                .HasFilter("[IsActive] = 1")
                .IsUnique();
        });

        modelBuilder.ConfigureCatalogReadModel();
        modelBuilder.ConfigureCheckoutDrafts();
        modelBuilder.ConfigureCustomerBookings();

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

    }
}
