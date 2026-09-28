using System.Security.Cryptography;
using System.Text;
using Ghseeli.DemoData;
using Ghseeli.BusinessApi.DataPartitioning;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.DataPartitioning;
using GhseeliApis.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.DemoData.Tests;

/// <summary>
/// Verifies repeatable relational seeding into isolated LocalDB databases.
/// </summary>
public sealed class DemoDatabaseSeederTests
{
    [Fact]
    public async Task SeedAsync_CreatesCorrelatedDataAndIsIdempotent()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var customerDatabase = $"GhseeliCustomer_FrontendDemo_{suffix}";
        var businessDatabase = $"GhseeliBusiness_FrontendDemo_{suffix}";
        var customerConnection = Connection(customerDatabase);
        var businessConnection = Connection(businessDatabase);

        try
        {
            var first = await DemoDatabaseSeeder.SeedAsync(customerConnection, businessConnection);
            var data = DemoDataDefinition.Create();
            var removedFavourite = data.Favourites[0];
            var unexpectedFavouriteId = Guid.NewGuid();
            await MutateFavouritesBeforeReseedAsync(
                customerConnection,
                removedFavourite.Id,
                unexpectedFavouriteId,
                data.Customers[0].Id,
                data.Companies[1].Id);
            var unexpectedBannerId = await MutateBannersBeforeReseedAsync(
                customerConnection,
                data);
            await DeleteReviewBeforeReseedAsync(
                customerConnection,
                data.Reviews[0].Id);
            await MutateAddonDefaultsBeforeReseedAsync(
                customerConnection,
                businessConnection);
            await ClearOfferingMetadataAsync(businessConnection, "ServiceOfferings");
            await ClearOfferingMetadataAsync(customerConnection, "CatalogOfferings");
            await ClearCategoryMetadataAsync(businessConnection, "ServiceCategories");
            await ClearCategoryMetadataAsync(customerConnection, "CatalogCategories");
            var second = await DemoDatabaseSeeder.SeedAsync(customerConnection, businessConnection);
            var third = await DemoDatabaseSeeder.SeedAsync(customerConnection, businessConnection);

            Assert.False(first.AlreadySeeded);
            Assert.True(second.AlreadySeeded);
            Assert.True(third.AlreadySeeded);
            Assert.Equal(5, first.CompanyCount);
            Assert.Equal(8, first.CustomerCount);
            Assert.Equal(12, first.CustomerBookingCount);
            Assert.Equal(12, first.BusinessReservationCount);
            Assert.Equal(first.CustomerBookingCount, first.BusinessReservationCount);

            await AssertOfferingMetadataParityAsync(customerConnection, businessConnection);
            await AssertCategoryMetadataParityAsync(customerConnection, businessConnection);
            await AssertCustomerCatalogLocalIdsAreDistinctFromSourceIdsAsync(customerConnection);
            await AssertFavouriteReconciliationAsync(
                customerConnection,
                unexpectedFavouriteId,
                preserveUnexpected: true);
            await AssertBannerReconciliationAsync(
                customerConnection,
                data,
                unexpectedBannerId);
            await AssertReviewReconciliationAsync(customerConnection, data);
            await AssertAddonDefaultParityAsync(
                customerConnection,
                businessConnection,
                data);
            await AssertDemoConfigurationAsync(customerConnection);
            await AssertVehiclePropagationAfterReloadAsync(
                customerConnection,
                businessConnection);
        }
        finally
        {
            await DropDatabaseAsync(customerDatabase);
            await DropDatabaseAsync(businessDatabase);
        }
    }

    [Fact]
    public async Task CleanupAsync_DeletesOnlyManifestDatasetAndAllowsReseeding()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var customerDatabase = $"GhseeliCustomer_FrontendDemo_{suffix}";
        var businessDatabase = $"GhseeliBusiness_FrontendDemo_{suffix}";
        var customerConnection = Connection(customerDatabase);
        var businessConnection = Connection(businessDatabase);

        try
        {
            await DemoDatabaseSeeder.SeedAsync(customerConnection, businessConnection);
            var data = DemoDataDefinition.Create();
            await MutateFavouritesBeforeReseedAsync(
                customerConnection,
                data.Favourites[0].Id,
                Guid.NewGuid(),
                data.Customers[0].Id,
                data.Companies[1].Id);

            var cleanup = await DemoDatabaseSeeder.CleanupAsync(
                customerConnection,
                businessConnection);
            var reseed = await DemoDatabaseSeeder.SeedAsync(
                customerConnection,
                businessConnection);

            Assert.Equal(8, cleanup.CustomerCount);
            Assert.Equal(5, cleanup.CompanyCount);
            Assert.Equal(12, cleanup.CustomerBookingCount);
            Assert.Equal(12, cleanup.BusinessReservationCount);
            Assert.False(reseed.AlreadySeeded);
            await AssertOfferingMetadataParityAsync(customerConnection, businessConnection);
            await AssertCategoryMetadataParityAsync(customerConnection, businessConnection);
            await AssertFavouriteReconciliationAsync(
                customerConnection,
                unexpectedFavouriteId: null,
                preserveUnexpected: false);
        }
        finally
        {
            await DropDatabaseAsync(customerDatabase);
            await DropDatabaseAsync(businessDatabase);
        }
    }

    private static string Connection(string database) =>
        $"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    private static Guid StableId(string scope, Guid value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"ghseeli-demo-{scope}-{value:N}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static async Task DeleteReviewBeforeReseedAsync(
        string connectionString,
        Guid reviewId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var partition = new CustomerDataPartitionContext();
        partition.SetTrustedPartition(DataPartitionNames.Demo);
        await using var context = new ApplicationDbContext(options, partition);
        await context.BusinessReviews
            .Where(value => value.Id == reviewId)
            .ExecuteDeleteAsync();
    }

    private static async Task AssertReviewReconciliationAsync(
        string connectionString,
        DemoDataset data)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var partition = new CustomerDataPartitionContext();
        partition.SetTrustedPartition(DataPartitionNames.Demo);
        await using var context = new ApplicationDbContext(options, partition);
        var actual = await context.BusinessReviews
            .AsNoTracking()
            .OrderBy(value => value.Id)
            .ToListAsync();

        Assert.Equal(data.Reviews.Count, actual.Count);
        foreach (var expected in data.Reviews)
        {
            var review = Assert.Single(actual, value => value.Id == expected.Id);
            Assert.Equal(
                StableId("customer-booking", expected.BookingReferenceId),
                review.CustomerBookingId);
            Assert.Equal(expected.CustomerId, review.UserId);
            Assert.Equal(expected.CompanyId, review.BusinessSourceId);
            Assert.Equal(expected.Rating, review.Rating);
            Assert.Equal(expected.Comment, review.Comment);
            Assert.Equal(expected.CreatedAtUtc, review.CreatedAtUtc);
            Assert.Equal(expected.CreatedAtUtc, review.UpdatedAtUtc);
        }
    }

    private static async Task AssertDemoConfigurationAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var context = new ApplicationDbContext(
            options,
            new CustomerDataPartitionContext());

        var configuration = await context.CustomerConfigurations
            .AsNoTracking()
            .SingleAsync();

        Assert.True(configuration.IsActive);
        Assert.Equal("support@ghseeli.example.test", configuration.SupportEmail);
        Assert.Equal("+972555000000", configuration.SupportPhone);
        Assert.Equal("غسيلي - بيئة التطوير", configuration.DisplayNameAr);
        Assert.Equal("https://ghseeli.example.test/privacy", configuration.PrivacyPolicyUrl);
        Assert.Equal("https://ghseeli.example.test/terms", configuration.TermsOfServiceUrl);
        Assert.False(configuration.IsMaintenanceModeEnabled);
    }

    private static async Task MutateAddonDefaultsBeforeReseedAsync(
        string customerConnection,
        string businessConnection)
    {
        foreach (var (connectionString, table) in new[]
                 {
                     (customerConnection, "CatalogAddonChoices"),
                     (businessConnection, "AddonChoices")
                 })
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE [{table}] SET [DefaultQuantity] = 1;";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task AssertAddonDefaultParityAsync(
        string customerConnection,
        string businessConnection,
        DemoDataset data)
    {
        var expected = data.Companies
            .SelectMany(company => company.Offerings)
            .SelectMany(offering => offering.AddonGroups)
            .SelectMany(group => group.Choices)
            .ToDictionary(choice => choice.Id, choice => choice.DefaultQuantity);

        await using var businessSql = new SqlConnection(businessConnection);
        await businessSql.OpenAsync();
        await using var businessCommand = businessSql.CreateCommand();
        businessCommand.CommandText = "SELECT [Id],[DefaultQuantity] FROM [AddonChoices];";
        await using var businessReader = await businessCommand.ExecuteReaderAsync();
        var businessActual = new Dictionary<Guid, int>();
        while (await businessReader.ReadAsync())
        {
            businessActual.Add(businessReader.GetGuid(0), businessReader.GetInt32(1));
        }

        await using var customerSql = new SqlConnection(customerConnection);
        await customerSql.OpenAsync();
        await using var customerCommand = customerSql.CreateCommand();
        customerCommand.CommandText =
            "SELECT [SourceAddonChoiceId],[DefaultQuantity] FROM [CatalogAddonChoices];";
        await using var customerReader = await customerCommand.ExecuteReaderAsync();
        var customerActual = new Dictionary<Guid, int>();
        while (await customerReader.ReadAsync())
        {
            customerActual.Add(customerReader.GetGuid(0), customerReader.GetInt32(1));
        }

        Assert.Equal(expected, businessActual);
        Assert.Equal(expected, customerActual);
    }

    private static async Task<Guid> MutateBannersBeforeReseedAsync(
        string connectionString,
        DemoDataset data)
    {
        var unexpectedId = Guid.NewGuid();
        var mutated = data.Banners[0];
        var deleted = data.Banners[1];
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE [Banners]
            SET [ImageUrl] = 'https://example.test/demo/banners/mutated.png',
                [DisplayOrder] = 9999,
                [IsActive] = 0
            WHERE [Id] = '{mutated.Id:D}';

            DELETE FROM [Banners] WHERE [Id] = '{deleted.Id:D}';

            INSERT INTO [Banners]
                ([Id],[ImageUrl],[DisplayOrder],[IsActive],[CreatedAtUtc],[UpdatedAtUtc],[IsDemo])
            VALUES
                ('{unexpectedId:D}','https://example.test/demo/banners/unexpected.png',
                 77,1,SYSUTCDATETIME(),SYSUTCDATETIME(),1);
            """;
        await command.ExecuteNonQueryAsync();
        return unexpectedId;
    }

    private static async Task AssertBannerReconciliationAsync(
        string connectionString,
        DemoDataset data,
        Guid unexpectedId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var partition = new CustomerDataPartitionContext();
        partition.SetTrustedPartition(DataPartitionNames.Demo);
        await using var context = new ApplicationDbContext(options, partition);
        var actual = await context.Banners
            .OrderBy(value => value.Id)
            .Select(value => new
            {
                value.Id,
                value.ImageUrl,
                value.DisplayOrder,
                value.IsActive,
                value.CreatedAtUtc,
                value.UpdatedAtUtc
            })
            .ToListAsync();

        Assert.Equal(data.Banners.Count, actual.Count);
        Assert.DoesNotContain(actual, value => value.Id == unexpectedId);
        foreach (var expected in data.Banners)
        {
            var banner = Assert.Single(actual, value => value.Id == expected.Id);
            Assert.Equal(expected.ImageUrl, banner.ImageUrl);
            Assert.Equal(expected.DisplayOrder, banner.DisplayOrder);
            Assert.Equal(expected.IsActive, banner.IsActive);
            Assert.Equal(expected.CreatedAtUtc, banner.CreatedAtUtc);
            Assert.Equal(expected.UpdatedAtUtc, banner.UpdatedAtUtc);
        }
    }

    private static async Task AssertOfferingMetadataParityAsync(
        string customerConnection,
        string businessConnection)
    {
        var dataset = DemoDataDefinition.Create();
        var businessMetadata = await ReadOfferingMetadataAsync(
            businessConnection,
            "ServiceOfferings",
            "Id");
        var customerMetadata = await ReadOfferingMetadataAsync(
            customerConnection,
            "CatalogOfferings",
            "SourceOfferingId");

        foreach (var company in dataset.Companies)
        {
            Assert.Single(
                company.Offerings,
                offering =>
                    offering.BadgeCode ==
                    Ghseeli.IntegrationContracts.BusinessCatalog.CatalogOfferingBadgeCode.MostRequested);
            Assert.Single(
                company.Offerings,
                offering => offering.QualifierAr is not null && offering.QualifierHe is null);
            Assert.Equal(2, company.Offerings.Count(offering =>
                offering.QualifierAr is null &&
                offering.QualifierHe is null &&
                offering.BadgeCode is null));

            foreach (var offering in company.Offerings)
            {
                var expected = (
                    offering.QualifierAr,
                    offering.QualifierHe,
                    offering.BadgeCode?.ToString());
                Assert.Equal(expected, businessMetadata[offering.Id]);
                Assert.Equal(expected, customerMetadata[offering.Id]);
            }
        }
    }

    private static async Task<Dictionary<Guid, (string? QualifierAr, string? QualifierHe, string? BadgeCode)>>
        ReadOfferingMetadataAsync(
            string connectionString,
            string table,
            string idColumn)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT [{idColumn}], QualifierAr, QualifierHe, BadgeCode FROM [{table}]";
        await using var reader = await command.ExecuteReaderAsync();
        var values = new Dictionary<Guid, (string?, string?, string?)>();
        while (await reader.ReadAsync())
        {
            values.Add(
                reader.GetGuid(0),
                (
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return values;
    }

    private static async Task ClearOfferingMetadataAsync(string connectionString, string table)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE [{table}] SET QualifierAr = NULL, QualifierHe = NULL, BadgeCode = NULL";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertCategoryMetadataParityAsync(
        string customerConnection,
        string businessConnection)
    {
        var dataset = DemoDataDefinition.Create();
        var businessMetadata = await ReadCategoryMetadataAsync(
            businessConnection,
            "ServiceCategories",
            "Id");
        var customerMetadata = await ReadCategoryMetadataAsync(
            customerConnection,
            "CatalogCategories",
            "SourceCategoryId");

        foreach (var category in dataset.Companies.SelectMany(company => company.Categories))
        {
            var expected = (category.ImageUrl, category.ColorHex);
            Assert.Equal(expected, businessMetadata[category.Id]);
            Assert.Equal(expected, customerMetadata[category.Id]);
        }
    }

    private static async Task<Dictionary<Guid, (string? ImageUrl, string? ColorHex)>>
        ReadCategoryMetadataAsync(
            string connectionString,
            string table,
            string idColumn)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT [{idColumn}], ImageUrl, ColorHex FROM [{table}]";
        await using var reader = await command.ExecuteReaderAsync();
        var values = new Dictionary<Guid, (string?, string?)>();
        while (await reader.ReadAsync())
        {
            values.Add(
                reader.GetGuid(0),
                (
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return values;
    }

    private static async Task ClearCategoryMetadataAsync(string connectionString, string table)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE [{table}] SET ImageUrl = NULL, ColorHex = NULL";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertCustomerCatalogLocalIdsAreDistinctFromSourceIdsAsync(
        string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var query in new[]
                 {
                     "SELECT COUNT(*) FROM [CatalogProviders] WHERE [Id] = [SourceCompanyId]",
                     "SELECT COUNT(*) FROM [CatalogCategories] WHERE [Id] = [SourceCategoryId]"
                 })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = query;
            Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
    }

    private static async Task AssertVehiclePropagationAfterReloadAsync(
        string customerConnection,
        string businessConnection)
    {
        var data = DemoDataDefinition.Create();
        var customerPartition = new CustomerDataPartitionContext();
        customerPartition.SetTrustedPartition(DataPartitionNames.Demo);
        var businessPartition = new BusinessDataPartitionContext();
        businessPartition.SetTrustedPartition(DataPartitionNames.Demo);

        await using var customer = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(customerConnection)
                .Options,
            customerPartition);
        await using var business = new BusinessDbContext(
            new DbContextOptionsBuilder<BusinessDbContext>()
                .UseSqlServer(businessConnection)
                .Options,
            businessPartition);

        var bookingFixture = data.Bookings[0];
        var bookingCustomer = data.Customers.Single(value =>
            value.Id == bookingFixture.CustomerId);
        var bookingVehicle = bookingCustomer.Vehicles[0];

        var savedVehicle = await customer.Vehicles
            .AsNoTracking()
            .SingleAsync(value => value.Id == bookingVehicle.Id);
        Assert.Equal(bookingVehicle.VehicleType, savedVehicle.VehicleType.ToString());
        Assert.Equal(bookingVehicle.ImageUrl, savedVehicle.ImageUrl);

        var booking = await customer.CustomerBookings
            .AsNoTracking()
            .SingleAsync(value =>
                value.PublicReference == bookingFixture.CustomerReferenceId);
        Assert.Equal(bookingVehicle.VehicleType, booking.VehicleType);
        Assert.Equal(bookingVehicle.ImageUrl, booking.VehicleImageUrl);

        var reviews = await customer.BusinessReviews
            .AsNoTracking()
            .OrderBy(value => value.CreatedAtUtc)
            .ToListAsync();
        Assert.Equal(data.Reviews.Count, reviews.Count);
        Assert.All(reviews, review =>
        {
            Assert.True(review.IsDemo);
            Assert.InRange(review.Rating, 1, 5);
        });

        var workOrder = await business.WorkOrders
            .AsNoTracking()
            .Include(value => value.VehicleDetails)
            .SingleAsync(value => value.Id == bookingFixture.BusinessWorkOrderId);
        Assert.Equal(bookingVehicle.VehicleType, workOrder.VehicleDetails.VehicleType);
        Assert.Equal(bookingVehicle.ImageUrl, workOrder.VehicleDetails.ImageUrl);

        var draftFixture = data.Drafts[0];
        var draftCustomer = data.Customers.Single(value =>
            value.Devices.Any(device => device.Id == draftFixture.DeviceId));
        var draftVehicle = draftCustomer.Vehicles[0];
        var draft = await customer.CheckoutDrafts
            .AsNoTracking()
            .SingleAsync(value => value.Id == draftFixture.Id);
        Assert.Equal(draftVehicle.VehicleType, draft.VehicleType);
        Assert.Equal(draftVehicle.ImageUrl, draft.VehicleImageUrl);
    }

    private static async Task MutateFavouritesBeforeReseedAsync(
        string connectionString,
        Guid removedFavouriteId,
        Guid unexpectedFavouriteId,
        Guid customerId,
        Guid companyId)
    {
        var partition = new CustomerDataPartitionContext();
        partition.SetTrustedPartition(DataPartitionNames.Demo);
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(connectionString)
                .Options,
            partition);
        var removed = await context.BusinessFavourites
            .AsNoTracking()
            .SingleAsync(value => value.Id == removedFavouriteId);
        await context.BusinessFavourites
            .Where(value => value.Id == removedFavouriteId)
            .ExecuteDeleteAsync();
        context.BusinessFavourites.Add(new GhseeliApis.Models.BusinessFavourite
        {
            Id = Guid.NewGuid(),
            UserId = removed.UserId,
            BusinessSourceId = removed.BusinessSourceId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        context.BusinessFavourites.Add(new GhseeliApis.Models.BusinessFavourite
        {
            Id = unexpectedFavouriteId,
            UserId = customerId,
            BusinessSourceId = companyId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
    }

    private static async Task AssertFavouriteReconciliationAsync(
        string connectionString,
        Guid? unexpectedFavouriteId,
        bool preserveUnexpected)
    {
        var data = DemoDataDefinition.Create();
        var partition = new CustomerDataPartitionContext();
        partition.SetTrustedPartition(DataPartitionNames.Demo);
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(connectionString)
                .Options,
            partition);
        var favourites = await context.BusinessFavourites
            .AsNoTracking()
            .OrderBy(value => value.Id)
            .ToListAsync();
        var expected = data.Favourites
            .Select(value => (value.Id, value.CustomerId, value.CompanyId))
            .OrderBy(value => value.Id.ToString())
            .ToArray();

        Assert.Equal(
            expected,
            favourites.Where(value => expected.Any(item => item.Id == value.Id))
                .Select(value => (value.Id, value.UserId, value.BusinessSourceId))
                .OrderBy(value => value.Id.ToString()));
        Assert.All(favourites, value => Assert.True(value.IsDemo));
        if (preserveUnexpected)
        {
            Assert.Equal(expected.Length + 1, favourites.Count);
            Assert.Single(favourites, value => value.Id == unexpectedFavouriteId);
        }
        else
        {
            Assert.Equal(expected.Length, favourites.Count);
        }
    }

    private static async Task DropDatabaseAsync(string database)
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(
            "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{database}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{database}]; END";
        await command.ExecuteNonQueryAsync();
    }
}
