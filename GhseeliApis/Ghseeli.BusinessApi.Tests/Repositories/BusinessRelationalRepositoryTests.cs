using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Repositories;
using Ghseeli.Common.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Ghseeli.BusinessApi.Tests.Repositories;

/// <summary>
/// Verifies relational constraints and aggregate-concurrency behavior with a real SQL Server provider.
/// </summary>
public class BusinessRelationalRepositoryTests
{
    [Fact]
    public async Task WorkOrderVehicleDetails_PersistReloadAndCascadeThroughRelationalMapping()
    {
        await using var database = await SqlServerBusinessDatabase.CreateAsync();
        var workOrderId = Guid.NewGuid();
        await database.ExecuteAsync(context =>
        {
            context.AppointmentReservations.Add(new AppointmentReservation
            {
                Id = Guid.NewGuid(),
                PublicId = Guid.NewGuid(),
                CustomerBookingReference = Guid.NewGuid(),
                OrderGuid = Guid.NewGuid(),
                RequestHash = "vehicle-details",
                BranchId = Guid.NewGuid(),
                CatalogVersion = 1,
                Currency = "ILS",
                Status = "Pending",
                StatusChangedAtUtc = DateTimeOffset.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                WorkOrder = new WorkOrder
                {
                    Id = workOrderId,
                    PublicId = Guid.NewGuid(),
                    Status = "Pending",
                    CustomerName = "Customer",
                    VehicleType = "SUV",
                    LicensePlate = "12-345-67",
                    VehicleMake = "Toyota",
                    VehicleModel = "RAV4",
                    VehicleColor = "Blue",
                    AddressLine = "Street",
                    CreatedAtUtc = DateTime.UtcNow
                }
            });
        });

        await using (var verificationContext = database.CreateContext())
        {
            var workOrder = await verificationContext.WorkOrders
                .SingleAsync(value => value.Id == workOrderId);
            workOrder.VehicleType.Should().Be("SUV");
            workOrder.LicensePlate.Should().Be("12-345-67");
            workOrder.VehicleMake.Should().Be("Toyota");
            workOrder.VehicleModel.Should().Be("RAV4");
            workOrder.VehicleColor.Should().Be("Blue");
            await verificationContext.WorkOrders
                .Where(value => value.Id == workOrderId)
                .ExecuteDeleteAsync();
        }

        await using var finalContext = database.CreateContext();
        (await finalContext.VehicleWorkOrderDetails
            .CountAsync(value => value.WorkOrderId == workOrderId))
            .Should().Be(0);
    }

    [Fact]
    public async Task AddCategoryAsync_WhenTwoWritersStartFromSameAggregateVersion_PreservesBothVersionIncrements()
    {
        await using var database = await SqlServerBusinessDatabase.CreateAsync();
        var companyId = Guid.NewGuid();

        await database.ExecuteAsync(context =>
        {
            context.Companies.Add(new Company
            {
                Id = companyId,
                NameAr = "شركة الاختبار",
                IsActive = true,
                CatalogVersion = 1
            });
        });

        await using var contextOne = database.CreateContext();
        await using var contextTwo = database.CreateContext();
        _ = await contextOne.Companies.SingleAsync(company => company.Id == companyId);
        _ = await contextTwo.Companies.SingleAsync(company => company.Id == companyId);

        var repositoryOne = new CatalogRepository(contextOne, CreateLogger().Object);
        var repositoryTwo = new CatalogRepository(contextTwo, CreateLogger().Object);

        await repositoryOne.AddCategoryAsync(new ServiceCategory
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            NameAr = "تنظيف",
            DisplayOrder = 0,
            IsActive = true
        });

        await repositoryTwo.AddCategoryAsync(new ServiceCategory
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            NameAr = "تلميع",
            DisplayOrder = 1,
            IsActive = true
        });

        await using var verificationContext = database.CreateContext();
        var company = await verificationContext.Companies.SingleAsync(item => item.Id == companyId);
        company.CatalogVersion.Should().Be(3);
        (await verificationContext.ServiceCategories.CountAsync(category => category.CompanyId == companyId))
            .Should()
            .Be(2);
    }

    [Fact]
    public async Task UpdateCategoryAsync_WhenConcurrentWriterChangesSameCategory_FailsCleanly()
    {
        await using var database = await SqlServerBusinessDatabase.CreateAsync();
        var companyId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await database.ExecuteAsync(context =>
        {
            var company = new Company
            {
                Id = companyId,
                NameAr = "شركة الاختبار",
                IsActive = true,
                CatalogVersion = 1
            };
            context.Add(company);
            context.ServiceCategories.Add(new ServiceCategory
            {
                Id = categoryId,
                CompanyId = companyId,
                Company = company,
                NameAr = "تنظيف",
                DisplayOrder = 0,
                IsActive = true
            });
        });

        await using var contextOne = database.CreateContext();
        await using var contextTwo = database.CreateContext();
        var repositoryOne = new CatalogRepository(contextOne, CreateLogger().Object);
        var repositoryTwo = new CatalogRepository(contextTwo, CreateLogger().Object);
        var categoryOne = await repositoryOne.GetCategoryByIdAsync(categoryId);
        var categoryTwo = await repositoryTwo.GetCategoryByIdAsync(categoryId);

        categoryOne.Should().NotBeNull();
        categoryTwo.Should().NotBeNull();

        categoryOne!.NameAr = "تغيير أول";
        categoryTwo!.NameAr = "تغيير ثان";

        await repositoryOne.UpdateCategoryAsync(categoryOne);

        var action = () => repositoryTwo.UpdateCategoryAsync(categoryTwo);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*conflict*");

        await using var verificationContext = database.CreateContext();
        var persistedCategory = await verificationContext.ServiceCategories.SingleAsync(item => item.Id == categoryId);
        var persistedCompany = await verificationContext.Companies.SingleAsync(item => item.Id == companyId);
        persistedCategory.NameAr.Should().Be("تغيير أول");
        persistedCompany.CatalogVersion.Should().Be(2);
    }

    [Fact]
    public async Task AddRecurringScheduleAsync_WhenConcurrentOverlappingWritersUseStaleAggregate_FailsCleanly()
    {
        await using var database = await SqlServerBusinessDatabase.CreateAsync();
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        await database.ExecuteAsync(context =>
        {
            var company = new Company
            {
                Id = companyId,
                NameAr = "شركة الاختبار",
                IsActive = true,
                CatalogVersion = 1
            };
            context.Add(company);
            context.Branches.Add(new Branch
            {
                Id = branchId,
                CompanyId = companyId,
                Company = company,
                NameAr = "الفرع الرئيسي",
                AddressAr = "العنوان",
                IsActive = true
            });
        });

        await using var contextOne = database.CreateContext();
        await using var contextTwo = database.CreateContext();
        _ = await contextOne.Companies.SingleAsync(company => company.Id == companyId);
        _ = await contextTwo.Companies.SingleAsync(company => company.Id == companyId);

        var repositoryOne = new AvailabilityRepository(contextOne, CreateLogger().Object);
        var repositoryTwo = new AvailabilityRepository(contextTwo, CreateLogger().Object);

        await repositoryOne.AddRecurringScheduleAsync(new BranchRecurringSchedule
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            DayOfWeek = DayOfWeek.Monday,
            StartLocalTime = TimeSpan.FromHours(9),
            EndLocalTime = TimeSpan.FromHours(12),
            SlotDurationMinutes = 30,
            Capacity = 2,
            IsActive = true
        }, companyId);

        var action = () => repositoryTwo.AddRecurringScheduleAsync(new BranchRecurringSchedule
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            DayOfWeek = DayOfWeek.Monday,
            StartLocalTime = TimeSpan.FromHours(10),
            EndLocalTime = TimeSpan.FromHours(11),
            SlotDurationMinutes = 30,
            Capacity = 2,
            IsActive = true
        }, companyId);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*conflict*");

        await using var verificationContext = database.CreateContext();
        (await verificationContext.BranchRecurringSchedules.CountAsync(item => item.BranchId == branchId))
            .Should()
            .Be(1);
        (await verificationContext.Companies.SingleAsync(item => item.Id == companyId)).CatalogVersion
            .Should()
            .Be(2);
    }

    [Fact]
    public async Task BranchAvailabilitySettings_UniquePerBranch_IsEnforcedRelationally()
    {
        await using var database = await SqlServerBusinessDatabase.CreateAsync();
        var branchId = await SeedCompanyAndBranchAsync(database);

        await database.ExecuteAsync(async context =>
        {
            context.BranchAvailabilitySettings.Add(new BranchAvailabilitySettings
            {
                Id = Guid.NewGuid(),
                BranchId = branchId,
                TimeZoneId = "UTC",
                MinimumLeadMinutes = 0,
                BookingHorizonDays = 30,
                IsActive = true
            });
            await context.SaveChangesAsync();

            context.BranchAvailabilitySettings.Add(new BranchAvailabilitySettings
            {
                Id = Guid.NewGuid(),
                BranchId = branchId,
                TimeZoneId = "UTC",
                MinimumLeadMinutes = 15,
                BookingHorizonDays = 60,
                IsActive = true
            });

            var action = () => context.SaveChangesAsync();
            await action.Should().ThrowAsync<DbUpdateException>();
        });
    }

    [Fact]
    public async Task BranchServiceArea_UniquePerBranch_IsEnforcedRelationally()
    {
        await using var database = await SqlServerBusinessDatabase.CreateAsync();
        var branchId = await SeedCompanyAndBranchAsync(database, withCoordinates: true);

        await database.ExecuteAsync(async context =>
        {
            context.BranchServiceAreas.Add(new BranchServiceArea
            {
                Id = Guid.NewGuid(),
                BranchId = branchId,
                RadiusKm = 10d,
                IsActive = true
            });
            await context.SaveChangesAsync();

            context.BranchServiceAreas.Add(new BranchServiceArea
            {
                Id = Guid.NewGuid(),
                BranchId = branchId,
                RadiusKm = 12d,
                IsActive = true
            });

            var action = () => context.SaveChangesAsync();
            await action.Should().ThrowAsync<DbUpdateException>();
        });
    }

    [Fact]
    public async Task BranchAvailabilityOverride_UniquePerBranchAndDate_IsEnforcedRelationally()
    {
        await using var database = await SqlServerBusinessDatabase.CreateAsync();
        var branchId = await SeedCompanyAndBranchAsync(database);
        var overrideDate = new DateOnly(2026, 8, 24);

        await database.ExecuteAsync(async context =>
        {
            context.BranchAvailabilityOverrides.Add(new BranchAvailabilityOverride
            {
                Id = Guid.NewGuid(),
                BranchId = branchId,
                OverrideDate = overrideDate,
                IsClosed = true,
                IsActive = true
            });
            await context.SaveChangesAsync();

            context.BranchAvailabilityOverrides.Add(new BranchAvailabilityOverride
            {
                Id = Guid.NewGuid(),
                BranchId = branchId,
                OverrideDate = overrideDate,
                IsClosed = false,
                StartLocalTime = TimeSpan.FromHours(9),
                EndLocalTime = TimeSpan.FromHours(12),
                SlotDurationMinutes = 30,
                Capacity = 2,
                IsActive = true
            });

            var action = () => context.SaveChangesAsync();
            await action.Should().ThrowAsync<DbUpdateException>();
        });
    }

    [Fact]
    public async Task DeletingCategory_CascadesToOfferingsGroupsAndChoices()
    {
        await using var database = await SqlServerBusinessDatabase.CreateAsync();
        var categoryId = Guid.NewGuid();

        await database.ExecuteAsync(context =>
        {
            var company = new Company
            {
                Id = Guid.NewGuid(),
                NameAr = "شركة الاختبار",
                IsActive = true
            };
            var category = new ServiceCategory
            {
                Id = categoryId,
                CompanyId = company.Id,
                Company = company,
                NameAr = "تنظيف",
                IsActive = true
            };
            var offering = new ServiceOffering
            {
                Id = Guid.NewGuid(),
                CategoryId = categoryId,
                Category = category,
                NameAr = "غسيل",
                BasePrice = 50m,
                DurationMinutes = 30,
                IsActive = true
            };
            var group = new AddonGroup
            {
                Id = Guid.NewGuid(),
                ServiceOfferingId = offering.Id,
                ServiceOffering = offering,
                NameAr = "إضافات",
                SelectionType = AddonSelectionType.MultipleChoice,
                MinimumSelections = 0,
                MaximumSelections = 1,
                IsActive = true
            };
            var choice = new AddonChoice
            {
                Id = Guid.NewGuid(),
                AddonGroupId = group.Id,
                AddonGroup = group,
                NameAr = "شمع",
                PriceAdjustment = 5m,
                DurationAdjustmentMinutes = 5,
                DefaultQuantity = 0,
                IsActive = true
            };

            group.Choices.Add(choice);
            offering.AddonGroups.Add(group);
            category.Offerings.Add(offering);
            context.Add(company);
            context.Add(category);
        });

        await database.ExecuteAsync(async context =>
        {
            var category = await context.ServiceCategories.SingleAsync(item => item.Id == categoryId);
            context.ServiceCategories.Remove(category);
            await context.SaveChangesAsync();
        });

        await database.ExecuteAsync(async context =>
        {
            (await context.ServiceCategories.CountAsync()).Should().Be(0);
            (await context.ServiceOfferings.CountAsync()).Should().Be(0);
            (await context.AddonGroups.CountAsync()).Should().Be(0);
            (await context.AddonChoices.CountAsync()).Should().Be(0);
        });
    }

    private static Mock<IAppLogger> CreateLogger()
    {
        return new Mock<IAppLogger>(MockBehavior.Loose);
    }

    private static async Task<Guid> SeedCompanyAndBranchAsync(
        SqlServerBusinessDatabase database,
        bool withCoordinates = false)
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        await database.ExecuteAsync(context =>
        {
            var company = new Company
            {
                Id = companyId,
                NameAr = "شركة الاختبار",
                IsActive = true,
                CatalogVersion = 1
            };

            context.Companies.Add(company);
            context.Branches.Add(new Branch
            {
                Id = branchId,
                CompanyId = companyId,
                Company = company,
                NameAr = "الفرع الرئيسي",
                AddressAr = "العنوان",
                Latitude = withCoordinates ? 24.7136 : null,
                Longitude = withCoordinates ? 46.6753 : null,
                IsActive = true
            });
        });

        return branchId;
    }

    private sealed class SqlServerBusinessDatabase : IAsyncDisposable
    {
        private readonly string _databaseName = $"GhseeliBusinessApiTests_{Guid.NewGuid():N}";

        public static async Task<SqlServerBusinessDatabase> CreateAsync()
        {
            var database = new SqlServerBusinessDatabase();
            await using var context = database.CreateContext();
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public BusinessDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<BusinessDbContext>()
                .UseSqlServer(ConnectionString)
                .Options;

            return new BusinessDbContext(options);
        }

        public async Task ExecuteAsync(Action<BusinessDbContext> action)
        {
            await using var context = CreateContext();
            action(context);
            await context.SaveChangesAsync();
        }

        public async Task ExecuteAsync(Func<BusinessDbContext, Task> action)
        {
            await using var context = CreateContext();
            await action(context);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var context = CreateContext();
                await context.Database.EnsureDeletedAsync();
            }
            catch (SqlException)
            {
            }
        }

        private string ConnectionString =>
            $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
    }
}
