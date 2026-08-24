using GhseeliApis.Models;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Persistence;

internal static class CustomerBookingConfiguration
{
    public static void ConfigureCustomerBookings(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BookingConfirmationAttempt>(entity =>
        {
            entity.ToTable("BookingConfirmationAttempts");
            entity.HasKey(attempt => attempt.Id);
            entity.Property(attempt => attempt.OrderGuid).IsRequired();
            entity.Property(attempt => attempt.BookingReference).IsRequired();
            entity.Property(attempt => attempt.ReservationRequestJson)
                .HasMaxLength(65536)
                .IsRequired();
            entity.Property(attempt => attempt.RowVersion).IsRowVersion();
            entity.HasIndex(attempt => attempt.OrderGuid).IsUnique();
            entity.HasIndex(attempt => attempt.BookingReference).IsUnique();
            entity.HasIndex(attempt => new { attempt.UserId, attempt.OwnerDeviceId });
        });

        modelBuilder.Entity<CustomerBooking>(entity =>
        {
            entity.ToTable("CustomerBookings");
            entity.HasKey(booking => booking.Id);
            entity.Property(booking => booking.PublicReference).IsRequired();
            entity.Property(booking => booking.OrderGuid).IsRequired();
            entity.Property(booking => booking.Status).HasMaxLength(32).IsRequired();
            entity.Property(booking => booking.BusinessStatusSequence).IsRequired();
            entity.Property(booking => booking.StatusChangedAtUtc).IsRequired();
            entity.Property(booking => booking.ProviderNameAr).HasMaxLength(200).IsRequired();
            entity.Property(booking => booking.ProviderNameHe).HasMaxLength(200);
            entity.Property(booking => booking.BranchNameAr).HasMaxLength(200).IsRequired();
            entity.Property(booking => booking.BranchNameHe).HasMaxLength(200);
            entity.Property(booking => booking.VehicleType).HasMaxLength(50).IsRequired();
            entity.Property(booking => booking.LicensePlate).HasMaxLength(50);
            entity.Property(booking => booking.VehicleMake).HasMaxLength(150);
            entity.Property(booking => booking.VehicleModel).HasMaxLength(150);
            entity.Property(booking => booking.VehicleColor).HasMaxLength(50);
            entity.Property(booking => booking.AddressLine).HasMaxLength(300).IsRequired();
            entity.Property(booking => booking.City).HasMaxLength(120);
            entity.Property(booking => booking.Area).HasMaxLength(120);
            entity.Property(booking => booking.Latitude).HasPrecision(9, 6);
            entity.Property(booking => booking.Longitude).HasPrecision(9, 6);
            entity.Property(booking => booking.Currency).HasMaxLength(10).IsRequired();
            entity.Property(booking => booking.BaseSubtotal).HasPrecision(18, 2);
            entity.Property(booking => booking.AddonSubtotal).HasPrecision(18, 2);
            entity.Property(booking => booking.ItemSubtotal).HasPrecision(18, 2);
            entity.Property(booking => booking.ServiceFee).HasPrecision(18, 2);
            entity.Property(booking => booking.ServiceFeeMode).HasMaxLength(32).IsRequired();
            entity.Property(booking => booking.ServiceFeeFlatAmount).HasPrecision(18, 2);
            entity.Property(booking => booking.ServiceFeePercentageRate).HasPrecision(9, 4);
            entity.Property(booking => booking.TaxableSubtotal).HasPrecision(18, 2);
            entity.Property(booking => booking.TaxRatePercent).HasPrecision(9, 4);
            entity.Property(booking => booking.Tax).HasPrecision(18, 2);
            entity.Property(booking => booking.GrandTotal).HasPrecision(18, 2);
            entity.Property(booking => booking.IsPaid).IsRequired();
            entity.Property(booking => booking.PaymentState)
                .HasMaxLength(32)
                .HasDefaultValue("Unpaid")
                .IsRequired();
            entity.Property(booking => booking.RowVersion).IsRowVersion();
            entity.HasOne(booking => booking.User)
                .WithMany()
                .HasForeignKey(booking => booking.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(booking => booking.PublicReference).IsUnique();
            entity.HasIndex(booking => booking.OrderGuid).IsUnique();
            entity.HasIndex(booking => booking.BusinessReservationId).IsUnique();
            entity.HasIndex(booking => booking.BusinessWorkOrderId).IsUnique();
            entity.HasIndex(booking => new { booking.UserId, booking.CreatedAtUtc });
            entity.HasIndex(booking => new { booking.OwnerDeviceId, booking.OrderGuid });
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_CustomerBookings_PaymentState",
                "[PaymentState] IN ('Unpaid','Pending','Completed','Failed','Refunded')"));
        });

        modelBuilder.Entity<CustomerPayment>(entity =>
        {
            entity.ToTable("CustomerPayments", table =>
            {
                table.HasCheckConstraint("CK_CustomerPayments_Amount", "[Amount] > 0");
                table.HasCheckConstraint("CK_CustomerPayments_MinorAmount", "[MinorAmount] > 0");
                table.HasCheckConstraint(
                    "CK_CustomerPayments_Currency",
                    "[Currency] IN ('ILS','USD','EUR')");
            });
            entity.HasKey(payment => payment.Id);
            entity.Property(payment => payment.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(payment => payment.Currency).HasMaxLength(3).IsRequired();
            entity.Property(payment => payment.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(payment => payment.RequestHash)
                .HasColumnType("char(64)")
                .IsRequired();
            entity.Property(payment => payment.StripeIdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(payment => payment.PaymentIntentId).HasMaxLength(200);
            entity.Property(payment => payment.ChargeId).HasMaxLength(200);
            entity.Property(payment => payment.ProviderStatus).HasMaxLength(64);
            entity.Property(payment => payment.ClientSecret).HasMaxLength(500);
            entity.Property(payment => payment.ProviderPublishableKey).HasMaxLength(200);
            entity.Property(payment => payment.IntentLeaseOwnerToken);
            entity.Property(payment => payment.IntentLeaseExpiresAtUtc);
            entity.Property(payment => payment.RowVersion).IsRowVersion();
            entity.HasOne(payment => payment.CustomerBooking)
                .WithOne(booking => booking.Payment)
                .HasForeignKey<CustomerPayment>(payment => payment.CustomerBookingId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(payment => payment.CustomerBookingId).IsUnique();
            entity.HasIndex(payment => new
            {
                payment.UserId,
                payment.OwnerDeviceId,
                payment.IdempotencyKey
            }).IsUnique();
            entity.HasIndex(payment => payment.PaymentIntentId)
                .IsUnique()
                .HasFilter("[PaymentIntentId] IS NOT NULL");
            entity.HasIndex(payment => payment.StripeIdempotencyKey).IsUnique();
            entity.HasIndex(payment => new { payment.UserId, payment.OwnerDeviceId, payment.Id });
            entity.HasIndex(payment => payment.IntentLeaseExpiresAtUtc)
                .HasFilter("[IntentLeaseOwnerToken] IS NOT NULL");
        });

        modelBuilder.Entity<StripeWebhookEventRecord>(entity =>
        {
            entity.ToTable("StripeWebhookEvents", table => table.HasCheckConstraint(
                "CK_StripeWebhookEvents_State",
                "[State] IN ('Processing','Completed','Quarantined','Deferred')"));
            entity.HasKey(value => value.EventId);
            entity.Property(value => value.EventId).HasMaxLength(200);
            entity.Property(value => value.BodyHash).HasColumnType("char(64)").IsRequired();
            entity.Property(value => value.EventType).HasMaxLength(100).IsRequired();
            entity.Property(value => value.State).HasMaxLength(16).IsRequired();
            entity.Property(value => value.DispositionReason).HasMaxLength(100);
            entity.Property(value => value.PaymentIntentId).HasMaxLength(200);
            entity.Property(value => value.ChargeId).HasMaxLength(200);
            entity.Property(value => value.Currency).HasMaxLength(3);
            entity.Property(value => value.RowVersion).IsRowVersion();
            entity.HasOne(value => value.CustomerPayment)
                .WithMany()
                .HasForeignKey(value => value.CustomerPaymentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => new { value.State, value.CreatedAtUtc });
            entity.HasIndex(value => new
            {
                value.CustomerPaymentId,
                value.State,
                value.ChargeId
            });
        });

        modelBuilder.Entity<CustomerPaymentIdempotencyRecord>(entity =>
        {
            entity.ToTable("CustomerPaymentIdempotencyRecords");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(value => value.RequestHash).HasColumnType("char(64)").IsRequired();
            entity.HasOne(value => value.CustomerPayment)
                .WithMany(value => value.IdempotencyRecords)
                .HasForeignKey(value => value.CustomerPaymentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => new
            {
                value.UserId,
                value.OwnerDeviceId,
                value.IdempotencyKey
            }).IsUnique();
            entity.HasIndex(value => value.CustomerPaymentId);
        });

        modelBuilder.Entity<CustomerInternalServiceNonce>(entity =>
        {
            entity.ToTable("CustomerInternalServiceNonces");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.ServiceId).HasMaxLength(64).IsRequired();
            entity.Property(value => value.Nonce).HasMaxLength(128).IsRequired();
            entity.HasIndex(value => new { value.ServiceId, value.Nonce }).IsUnique();
            entity.HasIndex(value => value.ExpiresAtUtc);
        });

        modelBuilder.Entity<CustomerInternalIdempotencyRecord>(entity =>
        {
            entity.ToTable("CustomerInternalIdempotencyRecords");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.ServiceId).HasMaxLength(64).IsRequired();
            entity.Property(value => value.Operation).HasMaxLength(64).IsRequired();
            entity.Property(value => value.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(value => value.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(value => value.OwnerToken);
            entity.Property(value => value.LeaseExpiresAtUtc);
            entity.Property(value => value.ResponseContentType).HasMaxLength(128);
            entity.Property(value => value.ResponseBody).HasColumnType("varbinary(max)");
            entity.HasIndex(value => new
            {
                value.ServiceId,
                value.Operation,
                value.IdempotencyKey
            }).IsUnique();
            entity.HasIndex(value => value.ExpiresAtUtc);
            entity.HasIndex(value => value.OwnerToken)
                .IsUnique()
                .HasFilter("[OwnerToken] IS NOT NULL");
            entity.HasIndex(value => new { value.State, value.LeaseExpiresAtUtc });
        });

        modelBuilder.Entity<ProcessedBookingStatusMessage>(entity =>
        {
            entity.ToTable("ProcessedBookingStatusMessages");
            entity.HasKey(value => value.EventId);
            entity.Property(value => value.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(32).IsRequired();
            entity.HasOne(value => value.CustomerBooking)
                .WithMany(value => value.ProcessedStatusMessages)
                .HasForeignKey(value => value.CustomerBookingId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => new { value.CustomerBookingId, value.Sequence });
        });

        modelBuilder.Entity<CustomerBookingItem>(entity =>
        {
            entity.ToTable("CustomerBookingItems");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ServiceNameAr).HasMaxLength(200).IsRequired();
            entity.Property(item => item.ServiceNameHe).HasMaxLength(200);
            entity.Property(item => item.BaseSubtotal).HasPrecision(18, 2);
            entity.Property(item => item.AddonSubtotal).HasPrecision(18, 2);
            entity.Property(item => item.ItemSubtotal).HasPrecision(18, 2);
            entity.HasOne(item => item.CustomerBooking)
                .WithMany(booking => booking.Items)
                .HasForeignKey(item => item.CustomerBookingId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.CustomerBookingId, item.DisplayOrder });
            entity.HasIndex(item => new { item.CustomerBookingId, item.OfferingSourceId }).IsUnique();
        });

        modelBuilder.Entity<CustomerBookingSelection>(entity =>
        {
            entity.ToTable("CustomerBookingSelections");
            entity.HasKey(selection => selection.Id);
            entity.Property(selection => selection.AddonGroupNameAr).HasMaxLength(200).IsRequired();
            entity.Property(selection => selection.AddonGroupNameHe).HasMaxLength(200);
            entity.Property(selection => selection.AddonChoiceNameAr).HasMaxLength(200).IsRequired();
            entity.Property(selection => selection.AddonChoiceNameHe).HasMaxLength(200);
            entity.Property(selection => selection.SelectionType).HasMaxLength(64).IsRequired();
            entity.Property(selection => selection.UnitPriceAdjustment).HasPrecision(18, 2);
            entity.Property(selection => selection.TotalPriceAdjustment).HasPrecision(18, 2);
            entity.HasOne(selection => selection.CustomerBookingItem)
                .WithMany(item => item.Selections)
                .HasForeignKey(selection => selection.CustomerBookingItemId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(selection => new { selection.CustomerBookingItemId, selection.DisplayOrder });
            entity.HasIndex(selection => new
            {
                selection.CustomerBookingItemId,
                selection.AddonChoiceSourceId
            }).IsUnique();
        });
    }
}
