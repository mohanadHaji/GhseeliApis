using Ghseeli.BusinessApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Persistence;

public class BusinessDbContext : IdentityDbContext<BusinessUser, IdentityRole<Guid>, Guid>
{
    public BusinessDbContext(DbContextOptions<BusinessDbContext> options)
        : base(options)
    {
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<BusinessVertical> BusinessVerticals => Set<BusinessVertical>();
    public DbSet<CompanyBusinessVertical> CompanyBusinessVerticals =>
        Set<CompanyBusinessVertical>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<ServiceOffering> ServiceOfferings => Set<ServiceOffering>();
    public DbSet<AddonGroup> AddonGroups => Set<AddonGroup>();
    public DbSet<AddonChoice> AddonChoices => Set<AddonChoice>();
    public DbSet<BranchAvailabilitySettings> BranchAvailabilitySettings =>
        Set<BranchAvailabilitySettings>();
    public DbSet<BranchRecurringSchedule> BranchRecurringSchedules =>
        Set<BranchRecurringSchedule>();
    public DbSet<BranchAvailabilityOverride> BranchAvailabilityOverrides =>
        Set<BranchAvailabilityOverride>();
    public DbSet<BranchServiceArea> BranchServiceAreas => Set<BranchServiceArea>();
    public DbSet<BusinessUserAssignment> BusinessUserAssignments =>
        Set<BusinessUserAssignment>();
    public DbSet<InternalServiceNonce> InternalServiceNonces => Set<InternalServiceNonce>();
    public DbSet<InternalServiceIdempotencyRecord> InternalServiceIdempotencyRecords =>
        Set<InternalServiceIdempotencyRecord>();
    public DbSet<AppointmentReservation> AppointmentReservations => Set<AppointmentReservation>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<VehicleWorkOrderDetails> VehicleWorkOrderDetails =>
        Set<VehicleWorkOrderDetails>();
    public DbSet<WorkOrderItem> WorkOrderItems => Set<WorkOrderItem>();
    public DbSet<WorkOrderSelection> WorkOrderSelections => Set<WorkOrderSelection>();
    public DbSet<BookingStatusOutboxMessage> BookingStatusOutboxMessages =>
        Set<BookingStatusOutboxMessage>();
    public DbSet<BookingStatusRequeueHistory> BookingStatusRequeueHistory =>
        Set<BookingStatusRequeueHistory>();

    public override int SaveChanges()
    {
        ApplyBusinessVerticalInvariants();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyBusinessVerticalInvariants();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        ApplyBusinessVerticalInvariants();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyBusinessVerticalInvariants();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyBusinessVerticalInvariants()
    {
        ChangeTracker.DetectChanges();
        BusinessVertical? inMemoryCarWash = null;
        if (!Database.IsRelational())
        {
            inMemoryCarWash = BusinessVerticals.Local.FirstOrDefault(vertical =>
                    vertical.Id == BusinessVerticalDefaults.CarWashId)
                ?? BusinessVerticals.FirstOrDefault(vertical =>
                    vertical.Id == BusinessVerticalDefaults.CarWashId);
            if (inMemoryCarWash is null)
            {
                inMemoryCarWash = new BusinessVertical
                {
                    Id = BusinessVerticalDefaults.CarWashId,
                    Code = BusinessVerticalDefaults.CarWashCode,
                    NameAr = "\u063a\u0633\u064a\u0644 \u0627\u0644\u0633\u064a\u0627\u0631\u0627\u062a",
                    NameHe = "\u05e9\u05d8\u05d9\u05e4\u05ea \u05e8\u05db\u05d1",
                    IsActive = true,
                    RegistrationEnabled = true,
                    CreatedAtUtc = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc)
                };
                BusinessVerticals.Add(inMemoryCarWash);
            }
        }

        foreach (var companyEntry in ChangeTracker.Entries<Company>()
                     .Where(entry => entry.State == EntityState.Added))
        {
            if (companyEntry.Entity.BusinessVerticals.Count == 0)
            {
                companyEntry.Entity.BusinessVerticals.Add(new CompanyBusinessVertical
                {
                    BusinessVerticalId = BusinessVerticalDefaults.CarWashId,
                    BusinessVertical = inMemoryCarWash!,
                    IsPrimary = true,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            if (companyEntry.Entity.BusinessVerticals.Count(assignment =>
                    assignment.IsPrimary && assignment.IsActive) != 1)
            {
                throw new InvalidOperationException(
                    "A new company must have exactly one active primary business vertical.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<AppointmentReservation>()
                     .Where(entry => entry.State == EntityState.Modified))
        {
            if (entry.Property(reservation => reservation.BusinessVerticalId).IsModified ||
                entry.Property(reservation => reservation.BusinessVerticalCode).IsModified)
            {
                throw new InvalidOperationException(
                    "Reservation business vertical snapshots are immutable.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<WorkOrder>()
                     .Where(entry => entry.State == EntityState.Modified))
        {
            if (entry.Property(workOrder => workOrder.BusinessVerticalId).IsModified ||
                entry.Property(workOrder => workOrder.BusinessVerticalCode).IsModified)
            {
                throw new InvalidOperationException(
                    "Work-order business vertical snapshots are immutable.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<WorkOrder>()
                     .Where(entry => entry.State == EntityState.Added))
        {
            if (entry.Entity.BusinessVerticalId != BusinessVerticalDefaults.CarWashId ||
                !string.Equals(
                    entry.Entity.BusinessVerticalCode,
                    BusinessVerticalDefaults.CarWashCode,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Work-order business vertical snapshots must identify car wash consistently.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<AppointmentReservation>()
                     .Where(entry => entry.State == EntityState.Added))
        {
            if (entry.Entity.BusinessVerticalId != BusinessVerticalDefaults.CarWashId ||
                !string.Equals(
                    entry.Entity.BusinessVerticalCode,
                    BusinessVerticalDefaults.CarWashCode,
                    StringComparison.Ordinal) ||
                    entry.Entity.WorkOrder is not null &&
                    (entry.Entity.WorkOrder.BusinessVerticalId != entry.Entity.BusinessVerticalId ||
                     !string.Equals(
                         entry.Entity.WorkOrder.BusinessVerticalCode,
                         entry.Entity.BusinessVerticalCode,
                         StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Reservation and work-order business vertical snapshots must identify car wash consistently.");
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(BusinessSchemaOptions.OwnedDefaultSchema);

        builder.Entity<BusinessUser>(entity =>
        {
            entity.Property(user => user.FullName)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(user => user.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()")
                .IsRequired();
        });

        builder.Entity<Company>(entity =>
        {
            entity.Property(company => company.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(company => company.NameHe).HasMaxLength(200);
            entity.Property(company => company.Phone).HasMaxLength(30);
            entity.Property(company => company.CatalogVersion)
                .HasDefaultValue(1L)
                .IsRequired();
            entity.Property(company => company.RowVersion)
                .IsRowVersion();
            entity.Property(company => company.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
        });

        builder.Entity<BusinessVertical>(entity =>
        {
            entity.ToTable("BusinessVerticals");
            entity.HasKey(vertical => vertical.Id);
            entity.Property(vertical => vertical.Code).HasMaxLength(64).IsRequired();
            entity.Property(vertical => vertical.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(vertical => vertical.NameHe).HasMaxLength(200);
            entity.Property(vertical => vertical.RowVersion).IsRowVersion();
            entity.HasIndex(vertical => vertical.Code).IsUnique();
            if (Database.IsRelational())
            {
                entity.HasData(new
                {
                    Id = BusinessVerticalDefaults.CarWashId,
                    Code = BusinessVerticalDefaults.CarWashCode,
                    NameAr = "\u063a\u0633\u064a\u0644 \u0627\u0644\u0633\u064a\u0627\u0631\u0627\u062a",
                    NameHe = "\u05e9\u05d8\u05d9\u05e4\u05ea \u05e8\u05db\u05d1",
                    IsActive = true,
                    RegistrationEnabled = true,
                    CreatedAtUtc = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc)
                });
            }
        });

        builder.Entity<CompanyBusinessVertical>(entity =>
        {
            entity.ToTable(
                "CompanyBusinessVerticals",
                table => table.HasCheckConstraint(
                    "CK_CompanyBusinessVerticals_PrimaryActive",
                    "[IsPrimary] = 0 OR [IsActive] = 1"));
            entity.HasKey(assignment => new
            {
                assignment.CompanyId,
                assignment.BusinessVerticalId
            });
            entity.HasOne(assignment => assignment.Company)
                .WithMany(company => company.BusinessVerticals)
                .HasForeignKey(assignment => assignment.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(assignment => assignment.BusinessVertical)
                .WithMany(vertical => vertical.Companies)
                .HasForeignKey(assignment => assignment.BusinessVerticalId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(assignment => assignment.CompanyId)
                .IsUnique()
                .HasFilter("[IsPrimary] = 1 AND [IsActive] = 1");
            entity.HasIndex(assignment => new
            {
                assignment.BusinessVerticalId,
                assignment.IsActive
            });
        });

        builder.Entity<Branch>(entity =>
        {
            entity.Property(branch => branch.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(branch => branch.NameHe).HasMaxLength(200);
            entity.Property(branch => branch.AddressAr).HasMaxLength(300).IsRequired();
            entity.Property(branch => branch.AddressHe).HasMaxLength(300);
            entity.Property(branch => branch.RowVersion)
                .IsRowVersion();
            entity.HasOne(branch => branch.Company)
                .WithMany(company => company.Branches)
                .HasForeignKey(branch => branch.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(branch => new { branch.CompanyId, branch.IsActive });
        });

        builder.Entity<BranchAvailabilitySettings>(entity =>
        {
            entity.Property(settings => settings.TimeZoneId)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(settings => settings.RowVersion)
                .IsRowVersion();
            entity.Property(settings => settings.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(settings => settings.Branch)
                .WithOne(branch => branch.AvailabilitySettings)
                .HasForeignKey<BranchAvailabilitySettings>(settings => settings.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(settings => settings.BranchId)
                .IsUnique();
        });

        builder.Entity<BranchRecurringSchedule>(entity =>
        {
            entity.Property(schedule => schedule.DayOfWeek)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(schedule => schedule.StartLocalTime)
                .HasColumnType("time")
                .IsRequired();
            entity.Property(schedule => schedule.EndLocalTime)
                .HasColumnType("time")
                .IsRequired();
            entity.Property(schedule => schedule.RowVersion)
                .IsRowVersion();
            entity.Property(schedule => schedule.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(schedule => schedule.Branch)
                .WithMany(branch => branch.RecurringSchedules)
                .HasForeignKey(schedule => schedule.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(schedule => new
            {
                schedule.BranchId,
                schedule.DayOfWeek,
                schedule.IsActive
            });
        });

        builder.Entity<BranchAvailabilityOverride>(entity =>
        {
            entity.Property(overrideItem => overrideItem.OverrideDate)
                .HasColumnType("date")
                .IsRequired();
            entity.Property(overrideItem => overrideItem.StartLocalTime)
                .HasColumnType("time");
            entity.Property(overrideItem => overrideItem.EndLocalTime)
                .HasColumnType("time");
            entity.Property(overrideItem => overrideItem.RowVersion)
                .IsRowVersion();
            entity.Property(overrideItem => overrideItem.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(overrideItem => overrideItem.Branch)
                .WithMany(branch => branch.AvailabilityOverrides)
                .HasForeignKey(overrideItem => overrideItem.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(overrideItem => new
            {
                overrideItem.BranchId,
                overrideItem.OverrideDate
            }).IsUnique();
        });

        builder.Entity<BranchServiceArea>(entity =>
        {
            entity.Property(serviceArea => serviceArea.RowVersion)
                .IsRowVersion();
            entity.Property(serviceArea => serviceArea.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(serviceArea => serviceArea.Branch)
                .WithOne(branch => branch.ServiceArea)
                .HasForeignKey<BranchServiceArea>(serviceArea => serviceArea.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(serviceArea => serviceArea.BranchId)
                .IsUnique();
        });

        builder.Entity<ServiceCategory>(entity =>
        {
            entity.Property(category => category.BusinessVerticalId)
                .HasDefaultValue(BusinessVerticalDefaults.CarWashId)
                .IsRequired();
            entity.Property(category => category.NameAr)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(category => category.NameHe)
                .HasMaxLength(200);
            entity.Property(category => category.DescriptionAr)
                .HasMaxLength(1000);
            entity.Property(category => category.DescriptionHe)
                .HasMaxLength(1000);
            entity.Property(category => category.RowVersion)
                .IsRowVersion();
            entity.Property(category => category.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(category => category.Company)
                .WithMany(company => company.Categories)
                .HasForeignKey(category => category.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(category => category.CompanyBusinessVertical)
                .WithMany()
                .HasForeignKey(category => new
                {
                    category.CompanyId,
                    category.BusinessVerticalId
                })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(category => category.BusinessVertical)
                .WithMany(vertical => vertical.Categories)
                .HasForeignKey(category => category.BusinessVerticalId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(category => new { category.CompanyId, category.DisplayOrder });
            entity.HasIndex(category => new { category.CompanyId, category.IsActive });
            entity.HasIndex(category => new
            {
                category.BusinessVerticalId,
                category.IsActive
            });
        });

        builder.Entity<ServiceOffering>(entity =>
        {
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
            entity.Property(offering => offering.RowVersion)
                .IsRowVersion();
            entity.Property(offering => offering.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(offering => offering.Category)
                .WithMany(category => category.Offerings)
                .HasForeignKey(offering => offering.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(offering => offering.Branch)
                .WithMany(branch => branch.ServiceOfferings)
                .HasForeignKey(offering => offering.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(offering => new { offering.CategoryId, offering.DisplayOrder });
            entity.HasIndex(offering => new { offering.CategoryId, offering.IsActive });
            entity.HasIndex(offering => new { offering.BranchId, offering.IsActive });
        });

        builder.Entity<AddonGroup>(entity =>
        {
            entity.Property(group => group.NameAr)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(group => group.NameHe)
                .HasMaxLength(200);
            entity.Property(group => group.DescriptionAr)
                .HasMaxLength(1000);
            entity.Property(group => group.DescriptionHe)
                .HasMaxLength(1000);
            entity.Property(group => group.SelectionType)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(group => group.RowVersion)
                .IsRowVersion();
            entity.Property(group => group.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(group => group.ServiceOffering)
                .WithMany(offering => offering.AddonGroups)
                .HasForeignKey(group => group.ServiceOfferingId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(group => new { group.ServiceOfferingId, group.DisplayOrder });
            entity.HasIndex(group => new { group.ServiceOfferingId, group.IsActive });
        });

        builder.Entity<AddonChoice>(entity =>
        {
            entity.Property(choice => choice.NameAr)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(choice => choice.NameHe)
                .HasMaxLength(200);
            entity.Property(choice => choice.DescriptionAr)
                .HasMaxLength(1000);
            entity.Property(choice => choice.DescriptionHe)
                .HasMaxLength(1000);
            entity.Property(choice => choice.PriceAdjustment)
                .HasPrecision(18, 2);
            entity.Property(choice => choice.RowVersion)
                .IsRowVersion();
            entity.Property(choice => choice.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(choice => choice.AddonGroup)
                .WithMany(group => group.Choices)
                .HasForeignKey(choice => choice.AddonGroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(choice => new { choice.AddonGroupId, choice.DisplayOrder });
            entity.HasIndex(choice => new { choice.AddonGroupId, choice.IsActive });
        });

        builder.Entity<BusinessUserAssignment>(entity =>
        {
            entity.HasOne(assignment => assignment.User)
                .WithMany(user => user.Assignments)
                .HasForeignKey(assignment => assignment.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(assignment => assignment.Company)
                .WithMany(company => company.Assignments)
                .HasForeignKey(assignment => assignment.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(assignment => assignment.Branch)
                .WithMany(branch => branch.Assignments)
                .HasForeignKey(assignment => assignment.BranchId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(assignment => new
            {
                assignment.UserId,
                assignment.CompanyId,
                assignment.BranchId
            }).IsUnique();
        });

        builder.Entity<InternalServiceNonce>(entity =>
        {
            entity.Property(record => record.ServiceId)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(record => record.Nonce)
                .HasMaxLength(128)
                .IsRequired();
            entity.HasIndex(record => new { record.ServiceId, record.Nonce })
                .IsUnique();
            entity.HasIndex(record => record.ExpiresAtUtc);
        });

        builder.Entity<InternalServiceIdempotencyRecord>(entity =>
        {
            entity.Property(record => record.ServiceId)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(record => record.Operation)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(record => record.IdempotencyKey)
                .HasMaxLength(128)
                .IsRequired();
            entity.Property(record => record.RequestHash)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(record => record.State)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(record => record.ContentType)
                .HasMaxLength(200);
            entity.Property(record => record.ResponseBody)
                .HasMaxLength(65536);
            entity.HasIndex(record => new
            {
                record.ServiceId,
                record.Operation,
                record.IdempotencyKey
            }).IsUnique();
            entity.HasIndex(record => record.ExpiresAtUtc);
        });

        builder.Entity<AppointmentReservation>(entity =>
        {
            entity.ToTable("AppointmentReservations");
            entity.HasKey(reservation => reservation.Id);
            entity.Property(reservation => reservation.PublicId).IsRequired();
            entity.Property(reservation => reservation.CustomerBookingReference).IsRequired();
            entity.Property(reservation => reservation.OrderGuid).IsRequired();
            entity.Property(reservation => reservation.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(reservation => reservation.BranchId).IsRequired();
            entity.Property(reservation => reservation.BusinessVerticalId)
                .HasDefaultValue(BusinessVerticalDefaults.CarWashId)
                .IsRequired();
            entity.Property(reservation => reservation.BusinessVerticalCode)
                .HasMaxLength(64)
                .HasDefaultValue(BusinessVerticalDefaults.CarWashCode)
                .IsRequired();
            entity.Property(reservation => reservation.Currency).HasMaxLength(10).IsRequired();
            entity.Property(reservation => reservation.ItemSubtotal).HasPrecision(18, 2);
            entity.Property(reservation => reservation.Status).HasMaxLength(32).IsRequired();
            entity.Property(reservation => reservation.StatusSequence).IsRequired();
            entity.Property(reservation => reservation.StatusChangedAtUtc).IsRequired();
            entity.Property(reservation => reservation.RowVersion).IsRowVersion();
            entity.HasIndex(reservation => reservation.PublicId).IsUnique();
            entity.HasIndex(reservation => reservation.CustomerBookingReference).IsUnique();
            entity.HasIndex(reservation => reservation.OrderGuid).IsUnique();
            entity.HasOne(reservation => reservation.BusinessVertical)
                .WithMany()
                .HasForeignKey(reservation => reservation.BusinessVerticalId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(reservation => new
            {
                reservation.BranchId,
                reservation.RequestedSlotStartUtc,
                reservation.RequestedSlotEndUtc,
                reservation.Status
            });
        });

        builder.Entity<WorkOrder>(entity =>
        {
            entity.ToTable("WorkOrders");
            entity.HasKey(workOrder => workOrder.Id);
            entity.Property(workOrder => workOrder.PublicId).IsRequired();
            entity.Property(workOrder => workOrder.Status).HasMaxLength(32).IsRequired();
            entity.Property(workOrder => workOrder.BusinessVerticalId)
                .HasDefaultValue(BusinessVerticalDefaults.CarWashId)
                .IsRequired();
            entity.Property(workOrder => workOrder.BusinessVerticalCode)
                .HasMaxLength(64)
                .HasDefaultValue(BusinessVerticalDefaults.CarWashCode)
                .IsRequired();
            entity.Property(workOrder => workOrder.RowVersion).IsRowVersion();
            entity.Property(workOrder => workOrder.CustomerName).HasMaxLength(150).IsRequired();
            entity.Property(workOrder => workOrder.CustomerEmail).HasMaxLength(254);
            entity.Property(workOrder => workOrder.CustomerPhone).HasMaxLength(32);
            entity.Property(workOrder => workOrder.AddressLine).HasMaxLength(300).IsRequired();
            entity.Property(workOrder => workOrder.City).HasMaxLength(120);
            entity.Property(workOrder => workOrder.Area).HasMaxLength(120);
            entity.Property(workOrder => workOrder.Latitude).HasPrecision(9, 6);
            entity.Property(workOrder => workOrder.Longitude).HasPrecision(9, 6);
            entity.HasOne(workOrder => workOrder.AppointmentReservation)
                .WithOne(reservation => reservation.WorkOrder)
                .HasForeignKey<WorkOrder>(workOrder => workOrder.AppointmentReservationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(workOrder => workOrder.BusinessVertical)
                .WithMany()
                .HasForeignKey(workOrder => workOrder.BusinessVerticalId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(workOrder => workOrder.PublicId).IsUnique();
            entity.HasIndex(workOrder => workOrder.AppointmentReservationId).IsUnique();
            entity.HasIndex(workOrder => workOrder.BusinessVerticalId);
            entity.Navigation(workOrder => workOrder.VehicleDetails).AutoInclude();
        });

        builder.Entity<VehicleWorkOrderDetails>(entity =>
        {
            entity.ToTable("VehicleWorkOrderDetails");
            entity.HasKey(details => details.WorkOrderId);
            entity.Property(details => details.VehicleType).HasMaxLength(50).IsRequired();
            entity.Property(details => details.LicensePlate).HasMaxLength(50);
            entity.Property(details => details.VehicleMake).HasMaxLength(150);
            entity.Property(details => details.VehicleModel).HasMaxLength(150);
            entity.Property(details => details.VehicleColor).HasMaxLength(50);
            entity.HasOne(details => details.WorkOrder)
                .WithOne(workOrder => workOrder.VehicleDetails)
                .HasForeignKey<VehicleWorkOrderDetails>(details => details.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BookingStatusOutboxMessage>(entity =>
        {
            entity.ToTable(
                "BookingStatusOutboxMessages",
                table =>
                {
                    table.HasCheckConstraint(
                        "CK_BookingStatusOutboxMessages_RequeueAudit",
                        "([RequeuedAtUtc] IS NULL AND [RequeuedByAdminUserId] IS NULL AND [RequeueRequestId] IS NULL) OR " +
                        "([RequeuedAtUtc] IS NOT NULL AND [RequeuedByAdminUserId] IS NOT NULL AND [RequeueRequestId] IS NOT NULL)");
                    table.HasCheckConstraint(
                        "CK_BookingStatusOutboxMessages_DeliveryGeneration",
                        "[DeliveryGeneration] >= 0");
                });
            entity.HasKey(message => message.Id);
            entity.Property(message => message.RequestJson).HasMaxLength(4096).IsRequired();
            entity.Property(message => message.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(message => message.WorkOrderPublicId).IsRequired();
            entity.Property(message => message.Status).HasMaxLength(32).IsRequired();
            entity.Property(message => message.Sequence).IsRequired();
            entity.Property(message => message.CorrelationId).HasMaxLength(64).IsRequired();
            entity.Property(message => message.DeliveryState).HasMaxLength(20).IsRequired();
            entity.Property(message => message.DeliveryGeneration).HasDefaultValue(0).IsRequired();
            entity.Property(message => message.LeaseOwner).HasMaxLength(128);
            entity.Property(message => message.LastErrorCode).HasMaxLength(64);
            entity.Property(message => message.RequeueRequestId).HasMaxLength(128);
            entity.Property(message => message.RowVersion).IsRowVersion();
            entity.HasOne(message => message.AppointmentReservation)
                .WithMany(reservation => reservation.StatusOutboxMessages)
                .HasForeignKey(message => message.AppointmentReservationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(message => new
            {
                message.DeliveryState,
                message.NextAttemptAtUtc,
                message.LeaseExpiresAtUtc
            });
            entity.HasIndex(message => new
            {
                message.AppointmentReservationId,
                message.Sequence
            }).IsUnique();
        });

        builder.Entity<BookingStatusRequeueHistory>(entity =>
        {
            entity.ToTable(
                "BookingStatusRequeueHistory",
                table => table.HasCheckConstraint(
                    "CK_BookingStatusRequeueHistory_Generation",
                    "[Generation] > 0"));
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Generation).IsRequired();
            entity.Property(value => value.AdminUserId).IsRequired();
            entity.Property(value => value.RequestId).HasMaxLength(128).IsRequired();
            entity.Property(value => value.RequeuedAtUtc).IsRequired();
            entity.HasOne(value => value.BookingStatusOutboxMessage)
                .WithMany(value => value.RequeueHistory)
                .HasForeignKey(value => value.BookingStatusOutboxMessageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => new
            {
                value.BookingStatusOutboxMessageId,
                value.Generation
            }).IsUnique();
            entity.HasIndex(value => new
            {
                value.BookingStatusOutboxMessageId,
                value.RequestId
            }).IsUnique();
        });

        builder.Entity<WorkOrderItem>(entity =>
        {
            entity.ToTable("WorkOrderItems");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.BaseSubtotal).HasPrecision(18, 2);
            entity.Property(item => item.AddonSubtotal).HasPrecision(18, 2);
            entity.Property(item => item.ItemSubtotal).HasPrecision(18, 2);
            entity.HasOne(item => item.WorkOrder)
                .WithMany(workOrder => workOrder.Items)
                .HasForeignKey(item => item.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.WorkOrderId, item.DisplayOrder });
            entity.HasIndex(item => new { item.WorkOrderId, item.OfferingId }).IsUnique();
        });

        builder.Entity<WorkOrderSelection>(entity =>
        {
            entity.ToTable("WorkOrderSelections");
            entity.HasKey(selection => selection.Id);
            entity.Property(selection => selection.SelectionType).HasMaxLength(64).IsRequired();
            entity.Property(selection => selection.UnitPriceAdjustment).HasPrecision(18, 2);
            entity.Property(selection => selection.TotalPriceAdjustment).HasPrecision(18, 2);
            entity.HasOne(selection => selection.WorkOrderItem)
                .WithMany(item => item.Selections)
                .HasForeignKey(selection => selection.WorkOrderItemId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(selection => new { selection.WorkOrderItemId, selection.DisplayOrder });
            entity.HasIndex(selection => new
            {
                selection.WorkOrderItemId,
                selection.AddonChoiceId
            }).IsUnique();
        });
    }
}
