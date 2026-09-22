using System.Security.Cryptography;
using System.Text;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DataPartitioning;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.Constants;
using GhseeliApis.DataPartitioning;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Devices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BusinessBranch = Ghseeli.BusinessApi.Models.Branch;
using BusinessUser = Ghseeli.BusinessApi.Models.BusinessUser;
using CustomerUser = GhseeliApis.Models.User;

namespace Ghseeli.DemoData;

public sealed record DemoSeedResult(
    bool AlreadySeeded,
    int CompanyCount,
    int CustomerCount,
    int CustomerBookingCount,
    int BusinessReservationCount);

public sealed record DemoCleanupResult(
    int CustomerCount,
    int CompanyCount,
    int CustomerBookingCount,
    int BusinessReservationCount);

public static class DemoDatabaseSeeder
{
    public static async Task<DemoSeedResult> SeedAsync(
        string customerConnectionString,
        string businessConnectionString,
        CancellationToken cancellationToken = default)
    {
        DemoConnectionGuard.Validate(customerConnectionString);
        DemoConnectionGuard.Validate(businessConnectionString);
        return await SeedCoreAsync(
            customerConnectionString,
            businessConnectionString,
            cancellationToken);
    }

    public static async Task<DemoSeedResult> SeedHostedAsync(
        string customerConnectionString,
        string businessConnectionString,
        string? repository,
        string? reference,
        string? githubActions,
        string? confirmation,
        CancellationToken cancellationToken = default)
    {
        HostedDemoConnectionGuard.Validate(
            customerConnectionString,
            businessConnectionString,
            repository,
            reference,
            githubActions,
            confirmation);
        return await SeedCoreAsync(
            customerConnectionString,
            businessConnectionString,
            cancellationToken);
    }

    private static async Task<DemoSeedResult> SeedCoreAsync(
        string customerConnectionString,
        string businessConnectionString,
        CancellationToken cancellationToken)
    {
        var data = DemoDataDefinition.Create();
        var businessOptions = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlServer(businessConnectionString)
            .Options;
        var customerOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(customerConnectionString)
            .Options;

        var businessPartition = new BusinessDataPartitionContext();
        businessPartition.SetTrustedPartition(DataPartitionNames.Demo);
        var customerPartition = new CustomerDataPartitionContext();
        customerPartition.SetTrustedPartition(DataPartitionNames.Demo);
        await using var business = new BusinessDbContext(businessOptions, businessPartition);
        await using var customer = new ApplicationDbContext(customerOptions, customerPartition);
        await business.Database.MigrateAsync(cancellationToken);
        await customer.Database.MigrateAsync(cancellationToken);

        var businessSeeded = await business.Companies
            .AnyAsync(company => company.Id == data.Companies[0].Id, cancellationToken);
        var customerSeeded = await customer.Users
            .AnyAsync(user => user.Id == data.Customers[0].Id, cancellationToken);

        if (businessSeeded != customerSeeded)
        {
            throw new InvalidOperationException(
                "Only one demo database contains the dataset. Use a fresh matched pair of Demo databases.");
        }

        if (!businessSeeded)
        {
            await SeedBusinessAsync(business, data, cancellationToken);
            await SeedCustomerAsync(customer, data, cancellationToken);
        }
        else
        {
            var refreshedAt = DateTimeOffset.UtcNow;
            var demoProviderIds = data.Companies.Select(company => company.Id).ToArray();
            var providers = await customer.CatalogProviders
                .Where(provider => demoProviderIds.Contains(provider.Id))
                .ToListAsync(cancellationToken);
            foreach (var provider in providers)
            {
                var company = data.Companies.Single(value => value.Id == provider.Id);
                provider.IsEnabled = true;
                provider.DisplayOrder = data.Companies.IndexOf(company) + 1;
                provider.SnapshotGeneratedAtUtc = refreshedAt;
                provider.LastSuccessfulRefreshAtUtc = refreshedAt;
                provider.LastAttemptedRefreshAtUtc = refreshedAt;
                provider.LastFailedRefreshAtUtc = null;
                provider.LastFailureCode = null;
            }

            var offeringsById = data.Companies
                .SelectMany(company => company.Offerings)
                .ToDictionary(offering => offering.Id);
            var offeringIds = offeringsById.Keys.ToArray();
            var businessOfferings = await business.ServiceOfferings
                .Where(offering => offeringIds.Contains(offering.Id))
                .ToListAsync(cancellationToken);
            var customerOfferings = await customer.CatalogOfferings
                .Where(offering => offeringIds.Contains(offering.SourceOfferingId))
                .ToListAsync(cancellationToken);

            foreach (var offering in businessOfferings)
            {
                var fixture = offeringsById[offering.Id];
                offering.QualifierAr = fixture.QualifierAr;
                offering.QualifierHe = fixture.QualifierHe;
                offering.BadgeCode = fixture.BadgeCode;
            }

            foreach (var offering in customerOfferings)
            {
                var fixture = offeringsById[offering.SourceOfferingId];
                offering.QualifierAr = fixture.QualifierAr;
                offering.QualifierHe = fixture.QualifierHe;
                offering.BadgeCode = fixture.BadgeCode;
            }

            await business.SaveChangesAsync(cancellationToken);
            await customer.SaveChangesAsync(cancellationToken);
        }

        var companyCount = await business.Companies
            .CountAsync(company => data.Companies.Select(value => value.Id).Contains(company.Id), cancellationToken);
        var customerCount = await customer.Users
            .CountAsync(user => data.Customers.Select(value => value.Id).Contains(user.Id), cancellationToken);
        var customerReferences = await customer.CustomerBookings
            .Where(booking => data.Bookings.Select(value => value.CustomerReferenceId).Contains(booking.PublicReference))
            .Select(booking => booking.PublicReference)
            .ToListAsync(cancellationToken);
        var businessReferences = await business.AppointmentReservations
            .Where(reservation => data.Bookings.Select(value => value.CustomerReferenceId).Contains(reservation.CustomerBookingReference))
            .Select(reservation => reservation.CustomerBookingReference)
            .ToListAsync(cancellationToken);

        if (companyCount != data.Companies.Count ||
            customerCount != data.Customers.Count ||
            customerReferences.Count != data.Bookings.Count ||
            businessReferences.Count != data.Bookings.Count ||
            !customerReferences.ToHashSet().SetEquals(businessReferences))
        {
            throw new InvalidOperationException(
                "The demo databases do not contain the expected complete correlated dataset.");
        }

        return new DemoSeedResult(
            businessSeeded,
            companyCount,
            customerCount,
            customerReferences.Count,
            businessReferences.Count);
    }

    public static async Task<DemoCleanupResult> CleanupAsync(
        string customerConnectionString,
        string businessConnectionString,
        CancellationToken cancellationToken = default)
    {
        DemoConnectionGuard.Validate(customerConnectionString);
        DemoConnectionGuard.Validate(businessConnectionString);
        var data = DemoDataDefinition.Create();
        var businessPartition = new BusinessDataPartitionContext();
        businessPartition.SetTrustedPartition(DataPartitionNames.Demo);
        var customerPartition = new CustomerDataPartitionContext();
        customerPartition.SetTrustedPartition(DataPartitionNames.Demo);
        await using var business = new BusinessDbContext(
            new DbContextOptionsBuilder<BusinessDbContext>()
                .UseSqlServer(businessConnectionString)
                .Options,
            businessPartition);
        await using var customer = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(customerConnectionString)
                .Options,
            customerPartition);

        var customerBookingIds = data.Bookings
            .Select(value => StableId("customer-booking", value.CustomerReferenceId))
            .ToArray();
        var draftIds = data.Drafts.Select(value => value.Id).ToArray();
        var providerIds = data.Companies.Select(value => value.Id).ToArray();
        var deviceIds = data.Customers
            .SelectMany(value => value.Devices)
            .Select(value => value.Id)
            .ToArray();
        var customerIds = data.Customers.Select(value => value.Id).ToArray();
        var reservationIds = data.Bookings
            .Select(value => value.BusinessReservationId)
            .ToArray();
        var businessUserIds = data.BusinessUsers.Select(value => value.Id).ToArray();

        await customer.CustomerPayments
            .Where(value => customerBookingIds.Contains(value.CustomerBookingId))
            .ExecuteDeleteAsync(cancellationToken);
        var deletedCustomerBookings = await customer.CustomerBookings
            .Where(value => value.IsDemo && customerBookingIds.Contains(value.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await customer.CheckoutDrafts
            .Where(value => value.IsDemo && draftIds.Contains(value.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await customer.CatalogProviders
            .Where(value => value.IsDemo && providerIds.Contains(value.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await customer.CustomerOtpChallenges
            .Where(value => value.IsDemo)
            .ExecuteDeleteAsync(cancellationToken);
        await customer.CustomerDevices
            .Where(value => value.IsDemo && deviceIds.Contains(value.Id))
            .ExecuteDeleteAsync(cancellationToken);
        var deletedCustomers = await customer.Users
            .Where(value => value.IsDemo && customerIds.Contains(value.Id))
            .ExecuteDeleteAsync(cancellationToken);

        var deletedReservations = await business.AppointmentReservations
            .Where(value => value.IsDemo && reservationIds.Contains(value.Id))
            .ExecuteDeleteAsync(cancellationToken);
        var deletedCompanies = await business.Companies
            .Where(value => value.IsDemo && providerIds.Contains(value.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await business.Users
            .Where(value => value.IsDemo && businessUserIds.Contains(value.Id))
            .ExecuteDeleteAsync(cancellationToken);

        return new(
            deletedCustomers,
            deletedCompanies,
            deletedCustomerBookings,
            deletedReservations);
    }

    private static async Task SeedBusinessAsync(
        BusinessDbContext context,
        DemoDataset data,
        CancellationToken cancellationToken)
    {
        var now = data.Metadata.GeneratedAtUtc.UtcDateTime;
        var companies = data.Companies.Select(company => new Company
        {
            Id = company.Id,
            NameAr = company.NameAr,
            NameHe = company.NameHe,
            DescriptionAr = "بيانات تجريبية لتطوير الواجهة الأمامية فقط",
            DescriptionHe = "נתוני הדגמה לפיתוח ממשק בלבד",
            ServiceAreaDescriptionAr = "نطاق خدمة تجريبي",
            ServiceAreaDescriptionHe = "אזור שירות ניסיוני",
            Phone = "+972555009999",
            IsActive = true,
            CatalogVersion = company.CatalogVersion,
            CreatedAt = now
        }).ToList();
        context.Companies.AddRange(companies);
        context.CompanyBusinessVerticals.AddRange(data.Companies.Select(company =>
            new CompanyBusinessVertical
            {
                CompanyId = company.Id,
                BusinessVerticalId = BusinessVerticalDefaults.CarWashId,
                IsPrimary = true,
                IsActive = true,
                CreatedAtUtc = now
            }));

        var branches = data.Companies.SelectMany(company => company.Branches)
            .Select(branch => new BusinessBranch
            {
                Id = branch.Id,
                CompanyId = branch.CompanyId,
                NameAr = branch.NameAr,
                NameHe = branch.NameHe,
                AddressAr = branch.AddressAr,
                AddressHe = branch.AddressHe,
                Latitude = branch.Latitude,
                Longitude = branch.Longitude,
                IsActive = true,
                CreatedAt = now
            }).ToList();
        context.Branches.AddRange(branches);
        context.BranchAvailabilitySettings.AddRange(data.Companies.SelectMany(company => company.Branches)
            .Select(branch => new BranchAvailabilitySettings
            {
                Id = StableId("availability-settings", branch.Id),
                BranchId = branch.Id,
                TimeZoneId = branch.TimeZoneId,
                MinimumLeadMinutes = 60,
                BookingHorizonDays = 30,
                IsActive = true,
                CreatedAt = now
            }));
        context.BranchServiceAreas.AddRange(data.Companies.SelectMany(company => company.Branches)
            .Select(branch => new BranchServiceArea
            {
                Id = StableId("service-area", branch.Id),
                BranchId = branch.Id,
                CenterLatitude = branch.Latitude,
                CenterLongitude = branch.Longitude,
                RadiusKm = branch.ServiceRadiusKm,
                IsActive = true,
                CreatedAt = now
            }));
        context.BranchRecurringSchedules.AddRange(
            data.Companies.SelectMany(company => company.Branches)
                .SelectMany(branch => Enumerable.Range(0, 6).Select(day => new BranchRecurringSchedule
                {
                    Id = StableId($"schedule-{day}", branch.Id),
                    BranchId = branch.Id,
                    DayOfWeek = (DayOfWeek)day,
                    StartLocalTime = TimeSpan.FromHours(day == 5 ? 9 : 8),
                    EndLocalTime = TimeSpan.FromHours(day == 5 ? 15 : 18),
                    SlotDurationMinutes = 30,
                    Capacity = 3,
                    IsActive = true,
                    CreatedAt = now
                })));
        context.BranchAvailabilityOverrides.AddRange(data.Companies.SelectMany(company => company.Branches)
            .Select(branch => new BranchAvailabilityOverride
            {
                Id = StableId("closure", branch.Id),
                BranchId = branch.Id,
                OverrideDate = DateOnly.FromDateTime(now.AddDays(21)),
                IsClosed = true,
                IsActive = true,
                CreatedAt = now
            }));

        context.ServiceCategories.AddRange(data.Companies.SelectMany(company => company.Categories)
            .Select((category, index) => new ServiceCategory
            {
                Id = category.Id,
                CompanyId = category.CompanyId,
                BusinessVerticalId = BusinessVerticalDefaults.CarWashId,
                NameAr = category.NameAr,
                NameHe = category.NameHe,
                DescriptionAr = "فئة تجريبية",
                DescriptionHe = "קטגוריה ניסיונית",
                DisplayOrder = index + 1,
                IsActive = true,
                CreatedAt = now
            }));
        context.ServiceOfferings.AddRange(data.Companies.SelectMany(company => company.Offerings)
            .Select((offering, index) => new ServiceOffering
            {
                Id = offering.Id,
                CategoryId = offering.CategoryId,
                BranchId = offering.BranchId,
                NameAr = offering.NameAr,
                NameHe = offering.NameHe,
                DescriptionAr = "خدمة تجريبية",
                DescriptionHe = "שירות ניסיוני",
                QualifierAr = offering.QualifierAr,
                QualifierHe = offering.QualifierHe,
                BadgeCode = offering.BadgeCode,
                BasePrice = offering.BasePrice,
                DurationMinutes = offering.DurationMinutes,
                ImageUrl = $"https://example.test/demo/services/{offering.ReferenceCode}.jpg",
                ReferenceCode = offering.ReferenceCode,
                DisplayOrder = index + 1,
                IsActive = true,
                CreatedAt = now
            }));
        context.AddonGroups.AddRange(data.Companies.SelectMany(company => company.Offerings)
            .SelectMany(offering => offering.AddonGroups)
            .Select((group, index) => new AddonGroup
            {
                Id = group.Id,
                ServiceOfferingId = group.OfferingId,
                NameAr = group.NameAr,
                NameHe = group.NameHe,
                DescriptionAr = "مجموعة إضافات تجريبية",
                DescriptionHe = "קבוצת תוספות ניסיונית",
                SelectionType = Enum.Parse<AddonSelectionType>(group.SelectionType),
                IsRequired = group.IsRequired,
                MinimumSelections = group.MinimumSelections,
                MaximumSelections = group.MaximumSelections,
                DisplayOrder = index + 1,
                IsActive = true,
                CreatedAt = now
            }));
        context.AddonChoices.AddRange(data.Companies.SelectMany(company => company.Offerings)
            .SelectMany(offering => offering.AddonGroups)
            .SelectMany(group => group.Choices)
            .Select((choice, index) => new AddonChoice
            {
                Id = choice.Id,
                AddonGroupId = choice.AddonGroupId,
                NameAr = choice.NameAr,
                NameHe = choice.NameHe,
                PriceAdjustment = choice.PriceAdjustment,
                DurationAdjustmentMinutes = choice.DurationAdjustmentMinutes,
                DefaultQuantity = choice.DefaultQuantity,
                DisplayOrder = index + 1,
                IsActive = true,
                CreatedAt = now
            }));

        var passwordHasher = new PasswordHasher<BusinessUser>();
        foreach (var demoUser in data.BusinessUsers)
        {
            var user = new BusinessUser
            {
                Id = demoUser.Id,
                FullName = demoUser.FullName,
                UserName = demoUser.Email,
                NormalizedUserName = demoUser.Email.ToUpperInvariant(),
                Email = demoUser.Email,
                NormalizedEmail = demoUser.Email.ToUpperInvariant(),
                EmailConfirmed = true,
                SecurityStamp = StableId("business-security", demoUser.Id).ToString("N"),
                ConcurrencyStamp = StableId("business-concurrency", demoUser.Id).ToString("N"),
                IsActive = true,
                CreatedAt = now
            };
            user.PasswordHash = passwordHasher.HashPassword(user, demoUser.Password);
            context.Users.Add(user);
        }

        var businessRoles = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            [BusinessRoles.Owner] = StableId("business-role", Guid.Parse("00000000-0000-0000-0000-000000000001")),
            [BusinessRoles.Employee] = StableId("business-role", Guid.Parse("00000000-0000-0000-0000-000000000002"))
        };
        foreach (var roleName in businessRoles.Keys.ToArray())
        {
            var normalizedName = roleName.ToUpperInvariant();
            var existingRoleId = await context.Roles
                .Where(value => value.NormalizedName == normalizedName)
                .Select(value => (Guid?)value.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (existingRoleId.HasValue)
            {
                businessRoles[roleName] = existingRoleId.Value;
                continue;
            }
            var roleId = businessRoles[roleName];
            context.Roles.Add(new IdentityRole<Guid>
            {
                Id = roleId,
                Name = roleName,
                NormalizedName = normalizedName,
                ConcurrencyStamp = StableId("business-role-stamp", roleId).ToString("N")
            });
        }

        foreach (var demoUser in data.BusinessUsers)
        {
            context.UserRoles.Add(new IdentityUserRole<Guid>
            {
                UserId = demoUser.Id,
                RoleId = businessRoles[demoUser.Role]
            });
            context.BusinessUserAssignments.Add(new BusinessUserAssignment
            {
                Id = StableId("business-assignment", demoUser.Id),
                UserId = demoUser.Id,
                CompanyId = demoUser.CompanyId,
                BranchId = demoUser.BranchId,
                Role = Enum.Parse<BusinessMembershipRole>(demoUser.Role),
                IsActive = true,
                CreatedAt = now
            });
        }

        foreach (var booking in data.Bookings)
        {
            var reservation = new AppointmentReservation
            {
                Id = booking.BusinessReservationId,
                PublicId = booking.BusinessReservationId,
                CustomerBookingReference = booking.CustomerReferenceId,
                OrderGuid = booking.OrderGuid,
                RequestHash = HashHex($"reservation-{booking.CustomerReferenceId}"),
                BranchId = booking.BranchId,
                BusinessVerticalId = BusinessVerticalDefaults.CarWashId,
                BusinessVerticalCode = BusinessVerticalDefaults.CarWashCode,
                CatalogVersion = data.Companies.Single(company => company.Id == booking.CompanyId).CatalogVersion,
                Currency = booking.Currency,
                ItemSubtotal = booking.ItemSubtotal,
                TotalDurationMinutes = booking.Items.Sum(item => item.DurationMinutes),
                RequestedSlotStartUtc = booking.RequestedSlotStartUtc.UtcDateTime,
                RequestedSlotEndUtc = booking.RequestedSlotStartUtc.AddMinutes(booking.Items.Sum(item => item.DurationMinutes)).UtcDateTime,
                Status = booking.Status,
                StatusSequence = 1,
                StatusChangedAtUtc = booking.RequestedSlotStartUtc.AddHours(-1),
                CreatedAtUtc = booking.RequestedSlotStartUtc.AddDays(-2).UtcDateTime
            };
            context.AppointmentReservations.Add(reservation);

            var customer = data.Customers.Single(value => value.Id == booking.CustomerId);
            var vehicle = customer.Vehicles[0];
            var address = customer.Addresses[0];
            var workOrder = new WorkOrder
            {
                Id = booking.BusinessWorkOrderId,
                PublicId = booking.BusinessWorkOrderId,
                AppointmentReservationId = booking.BusinessReservationId,
                Status = booking.Status,
                BusinessVerticalId = BusinessVerticalDefaults.CarWashId,
                BusinessVerticalCode = BusinessVerticalDefaults.CarWashCode,
                CustomerName = customer.FullName,
                CustomerEmail = customer.Email,
                CustomerPhone = customer.Phone,
                AddressLine = address.AddressLine,
                City = address.City,
                Area = address.Area,
                Latitude = (decimal)address.Latitude,
                Longitude = (decimal)address.Longitude,
                CreatedAtUtc = booking.RequestedSlotStartUtc.AddDays(-2).UtcDateTime,
                VehicleDetails = new VehicleWorkOrderDetails
                {
                    WorkOrderId = booking.BusinessWorkOrderId,
                    VehicleType = "Car",
                    LicensePlate = vehicle.LicensePlate,
                    VehicleMake = vehicle.Make,
                    VehicleModel = vehicle.Model,
                    VehicleColor = vehicle.Color
                }
            };
            foreach (var (item, index) in booking.Items.Select((value, itemIndex) => (value, itemIndex)))
            {
                workOrder.Items.Add(new WorkOrderItem
                {
                    Id = StableId($"work-order-item-{index}", booking.BusinessWorkOrderId),
                    OfferingId = item.OfferingId,
                    DisplayOrder = index + 1,
                    BaseSubtotal = item.ItemSubtotal,
                    AddonSubtotal = 0,
                    ItemSubtotal = item.ItemSubtotal,
                    TotalDurationMinutes = item.DurationMinutes
                });
            }
            context.WorkOrders.Add(workOrder);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedCustomerAsync(
        ApplicationDbContext context,
        DemoDataset data,
        CancellationToken cancellationToken)
    {
        var now = data.Metadata.GeneratedAtUtc;
        var refreshedAt = DateTimeOffset.UtcNow;
        var passwordHasher = new PasswordHasher<CustomerUser>();
        var normalizedRoleName = AppRoles.User.ToUpperInvariant();
        var roleId = await context.Roles
            .Where(value => value.NormalizedName == normalizedRoleName)
            .Select(value => (Guid?)value.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? StableId(
                "customer-role",
                Guid.Parse("00000000-0000-0000-0000-000000000001"));
        if (!await context.Roles.AnyAsync(value => value.Id == roleId, cancellationToken))
        {
            context.Roles.Add(new IdentityRole<Guid>
            {
                Id = roleId,
                Name = AppRoles.User,
                NormalizedName = normalizedRoleName,
                ConcurrencyStamp = StableId("customer-role-stamp", roleId).ToString("N")
            });
        }

        foreach (var customer in data.Customers)
        {
            var user = new CustomerUser
            {
                Id = customer.Id,
                FullName = customer.FullName,
                UserName = customer.Email,
                NormalizedUserName = customer.Email.ToUpperInvariant(),
                Email = customer.Email,
                NormalizedEmail = customer.Email.ToUpperInvariant(),
                EmailConfirmed = true,
                Phone = customer.Phone,
                PhoneNumber = customer.Phone,
                PhoneNumberConfirmed = true,
                SecurityStamp = StableId("customer-security", customer.Id).ToString("N"),
                ConcurrencyStamp = StableId("customer-concurrency", customer.Id).ToString("N"),
                IsActive = true,
                CreatedAt = now.UtcDateTime
            };
            user.PasswordHash = passwordHasher.HashPassword(user, customer.Password);
            context.Users.Add(user);
            context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = customer.Id, RoleId = roleId });
            context.CustomerDevices.AddRange(customer.Devices.Select(device => new CustomerDevice
            {
                Id = device.Id,
                InstallationId = device.InstallationId,
                UserId = customer.Id,
                Platform = device.Platform,
                AppVersion = device.AppVersion,
                FcmToken = $"DEMO_FCM_{device.Id:N}",
                TokenHash = DeviceTokenHasher.Hash(device.Token),
                CreatedAt = now,
                UpdatedAt = now,
                LastSeenAt = now,
                ExpiresAt = now.AddYears(5),
                IsActive = device.IsActive
            }));
            context.Vehicles.AddRange(customer.Vehicles.Select(vehicle => new Vehicle
            {
                Id = vehicle.Id,
                UserId = customer.Id,
                Make = vehicle.Make,
                Model = vehicle.Model,
                Year = vehicle.Year,
                LicensePlate = vehicle.LicensePlate,
                Color = vehicle.Color
            }));
            context.UserAddresses.AddRange(customer.Addresses.Select(address => new UserAddress
            {
                Id = address.Id,
                UserId = customer.Id,
                AddressLine = address.AddressLine,
                City = address.City,
                Area = address.Area,
                Latitude = address.Latitude,
                Longitude = address.Longitude,
                IsPrimary = address.IsPrimary
            }));
        }

        foreach (var company in data.Companies)
        {
            context.CatalogProviders.Add(new CatalogProviderReadModel
            {
                Id = company.Id,
                SourceCompanyId = company.Id,
                BusinessVerticalCode = BusinessVerticalSnapshotDefaults.CarWashCode,
                IsEnabled = true,
                DisplayOrder = data.Companies.IndexOf(company) + 1,
                NameAr = company.NameAr,
                NameHe = company.NameHe,
                DescriptionAr = "مزود تجريبي",
                DescriptionHe = "ספק ניסיוני",
                Phone = "+972555009999",
                CatalogVersion = company.CatalogVersion,
                SnapshotHash = HashHex($"catalog-{company.Id}"),
                SnapshotGeneratedAtUtc = refreshedAt,
                LastSuccessfulRefreshAtUtc = refreshedAt,
                LastAttemptedRefreshAtUtc = refreshedAt
            });
            context.CatalogBranches.AddRange(company.Branches.Select((branch, index) => new CatalogBranchReadModel
            {
                Id = branch.Id,
                SourceBranchId = branch.Id,
                ProviderId = company.Id,
                NameAr = branch.NameAr,
                NameHe = branch.NameHe,
                AddressAr = branch.AddressAr,
                AddressHe = branch.AddressHe,
                Latitude = branch.Latitude,
                Longitude = branch.Longitude,
                HasPublishedServiceArea = true,
                UsesBranchCoordinates = true,
                ServiceAreaCenterLatitude = branch.Latitude,
                ServiceAreaCenterLongitude = branch.Longitude,
                ServiceAreaRadiusKm = branch.ServiceRadiusKm,
                AvailabilitySnapshotJson = "{\"datasetType\":\"demo\",\"slotDurationMinutes\":30,\"capacity\":3}",
                DisplayOrder = index + 1
            }));
            context.CatalogCategories.AddRange(company.Categories.Select((category, index) => new CatalogCategoryReadModel
            {
                Id = category.Id,
                SourceCategoryId = category.Id,
                ProviderId = company.Id,
                NameAr = category.NameAr,
                NameHe = category.NameHe,
                DescriptionAr = "فئة تجريبية",
                DescriptionHe = "קטגוריה ניסיונית",
                DisplayOrder = index + 1
            }));
            context.CatalogOfferings.AddRange(company.Offerings.Select((offering, index) => new CatalogOfferingReadModel
            {
                Id = offering.Id,
                SourceOfferingId = offering.Id,
                CategoryId = offering.CategoryId,
                BranchId = offering.BranchId,
                NameAr = offering.NameAr,
                NameHe = offering.NameHe,
                DescriptionAr = "خدمة تجريبية",
                DescriptionHe = "שירות ניסיוני",
                QualifierAr = offering.QualifierAr,
                QualifierHe = offering.QualifierHe,
                BadgeCode = offering.BadgeCode,
                BasePrice = offering.BasePrice,
                DurationMinutes = offering.DurationMinutes,
                ImageUrl = $"https://example.test/demo/services/{offering.ReferenceCode}.jpg",
                ReferenceCode = offering.ReferenceCode,
                DisplayOrder = index + 1
            }));
            context.CatalogAddonGroups.AddRange(company.Offerings.SelectMany(offering => offering.AddonGroups)
                .Select((group, index) => new CatalogAddonGroupReadModel
                {
                    Id = group.Id,
                    SourceAddonGroupId = group.Id,
                    OfferingId = group.OfferingId,
                    NameAr = group.NameAr,
                    NameHe = group.NameHe,
                    DescriptionAr = "مجموعة تجريبية",
                    DescriptionHe = "קבוצה ניסיונית",
                    SelectionType = group.SelectionType,
                    IsRequired = group.IsRequired,
                    MinimumSelections = group.MinimumSelections,
                    MaximumSelections = group.MaximumSelections,
                    DisplayOrder = index + 1
                }));
            context.CatalogAddonChoices.AddRange(company.Offerings.SelectMany(offering => offering.AddonGroups)
                .SelectMany(group => group.Choices)
                .Select((choice, index) => new CatalogAddonChoiceReadModel
                {
                    Id = choice.Id,
                    SourceAddonChoiceId = choice.Id,
                    AddonGroupId = choice.AddonGroupId,
                    NameAr = choice.NameAr,
                    NameHe = choice.NameHe,
                    PriceAdjustment = choice.PriceAdjustment,
                    DurationAdjustmentMinutes = choice.DurationAdjustmentMinutes,
                    DefaultQuantity = choice.DefaultQuantity,
                    DisplayOrder = index + 1
                }));
        }

        foreach (var draft in data.Drafts)
        {
            var entity = new CheckoutDraft
            {
                Id = draft.Id,
                OrderGuid = draft.OrderGuid,
                OwnerDeviceId = draft.DeviceId,
                BusinessSourceId = draft.CompanyId,
                BranchSourceId = draft.BranchId,
                CatalogVersion = data.Companies.Single(company => company.Id == draft.CompanyId).CatalogVersion,
                PublicVersion = 1,
                RequestedSlotStartUtc = draft.RequestedSlotStartUtc,
                VehicleType = "Car",
                LicensePlate = "DEMO-DRAFT",
                VehicleMake = "Demo",
                VehicleModel = "Fixture",
                VehicleColor = "Silver",
                AddressLine = "[DEMO] Draft address",
                City = "Ramallah",
                Area = "Demo Area",
                Latitude = 31.9038m,
                Longitude = 35.2034m,
                RequiresReprice = draft.RequiresReprice,
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now,
                ExpiresAt = draft.ExpiresAtUtc
            };
            foreach (var (item, index) in draft.Items.Select((value, itemIndex) => (value, itemIndex)))
            {
                entity.Items.Add(new CheckoutDraftItem
                {
                    Id = StableId($"draft-item-{index}", draft.Id),
                    OfferingSourceId = item.OfferingId,
                    DisplayOrder = index + 1
                });
            }
            context.CheckoutDrafts.Add(entity);
        }

        foreach (var booking in data.Bookings)
        {
            var customer = data.Customers.Single(value => value.Id == booking.CustomerId);
            var company = data.Companies.Single(value => value.Id == booking.CompanyId);
            var branch = company.Branches.Single(value => value.Id == booking.BranchId);
            var vehicle = customer.Vehicles[0];
            var address = customer.Addresses[0];
            var paid = booking.Payment?.Status == nameof(PaymentStatus.Completed);
            var entity = new CustomerBooking
            {
                Id = StableId("customer-booking", booking.CustomerReferenceId),
                PublicReference = booking.CustomerReferenceId,
                OrderGuid = booking.OrderGuid,
                UserId = booking.CustomerId,
                OwnerDeviceId = booking.DeviceId,
                BusinessReservationId = booking.BusinessReservationId,
                BusinessWorkOrderId = booking.BusinessWorkOrderId,
                BusinessSourceId = booking.CompanyId,
                BusinessVerticalCode = BusinessVerticalSnapshotDefaults.CarWashCode,
                BranchSourceId = booking.BranchId,
                CatalogVersion = company.CatalogVersion,
                ConfirmedDraftVersion = 1,
                Status = booking.Status,
                BusinessStatusSequence = 1,
                StatusChangedAtUtc = booking.RequestedSlotStartUtc.AddHours(-1),
                RequestedSlotStartUtc = booking.RequestedSlotStartUtc,
                RequestedSlotEndUtc = booking.RequestedSlotStartUtc.AddMinutes(booking.Items.Sum(item => item.DurationMinutes)),
                ProviderNameAr = company.NameAr,
                ProviderNameHe = company.NameHe,
                BranchNameAr = branch.NameAr,
                BranchNameHe = branch.NameHe,
                VehicleType = "Car",
                LicensePlate = vehicle.LicensePlate,
                VehicleMake = vehicle.Make,
                VehicleModel = vehicle.Model,
                VehicleColor = vehicle.Color,
                AddressLine = address.AddressLine,
                City = address.City,
                Area = address.Area,
                Latitude = (decimal)address.Latitude,
                Longitude = (decimal)address.Longitude,
                Currency = booking.Currency,
                BaseSubtotal = booking.ItemSubtotal,
                AddonSubtotal = 0,
                ItemSubtotal = booking.ItemSubtotal,
                ServiceFee = booking.ServiceFee,
                ServiceFeeMode = "Flat",
                ServiceFeeFlatAmount = booking.ServiceFee,
                TaxableSubtotal = booking.ItemSubtotal + booking.ServiceFee,
                TaxRatePercent = 16,
                TaxAppliesToServiceFee = true,
                Tax = booking.Tax,
                GrandTotal = booking.GrandTotal,
                IsPaid = paid,
                PaymentState = booking.Payment?.Status ?? "Unpaid",
                TotalDurationMinutes = booking.Items.Sum(item => item.DurationMinutes),
                QuotedAtUtc = booking.RequestedSlotStartUtc.AddDays(-2),
                CreatedAtUtc = booking.RequestedSlotStartUtc.AddDays(-2)
            };
            foreach (var (item, index) in booking.Items.Select((value, itemIndex) => (value, itemIndex)))
            {
                entity.Items.Add(new CustomerBookingItem
                {
                    Id = StableId($"customer-booking-item-{index}", booking.CustomerReferenceId),
                    OfferingSourceId = item.OfferingId,
                    ServiceNameAr = item.NameAr,
                    ServiceNameHe = item.NameHe,
                    DisplayOrder = index + 1,
                    BaseSubtotal = item.ItemSubtotal,
                    AddonSubtotal = 0,
                    ItemSubtotal = item.ItemSubtotal,
                    TotalDurationMinutes = item.DurationMinutes
                });
            }
            context.CustomerBookings.Add(entity);

            if (booking.Payment is not null)
            {
                context.CustomerPayments.Add(new CustomerPayment
                {
                    Id = booking.Payment.Id,
                    CustomerBookingId = entity.Id,
                    UserId = booking.CustomerId,
                    OwnerDeviceId = booking.DeviceId,
                    Amount = booking.Payment.Amount,
                    MinorAmount = decimal.ToInt64(booking.Payment.Amount * 100m),
                    Currency = booking.Payment.Currency,
                    Method = PaymentMethod.Card,
                    Status = Enum.Parse<PaymentStatus>(booking.Payment.Status),
                    IdempotencyKey = $"demo-payment-{booking.Payment.Id:N}",
                    RequestHash = HashHex($"payment-{booking.Payment.Id}"),
                    Provider = "Lahza",
                    ProviderReference = booking.Payment.ProviderReference,
                    ProviderTransactionId = booking.Payment.ProviderTransactionId,
                    ProviderStatus = booking.Payment.Status.ToLowerInvariant(),
                    CheckoutUrl = $"https://example.test/demo/checkout/{booking.Payment.ProviderReference}",
                    InitializationState = PaymentInitializationStates.Initialized,
                    CreatedAtUtc = booking.RequestedSlotStartUtc.AddDays(-2),
                    UpdatedAtUtc = booking.RequestedSlotStartUtc.AddDays(-1)
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static Guid StableId(string scope, Guid value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"ghseeli-demo-{scope}-{value:N}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string HashHex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
