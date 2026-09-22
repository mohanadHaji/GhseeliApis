using Ghseeli.DemoData;
using Microsoft.Data.SqlClient;

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
            await ClearOfferingMetadataAsync(businessConnection, "ServiceOfferings");
            await ClearOfferingMetadataAsync(customerConnection, "CatalogOfferings");
            var second = await DemoDatabaseSeeder.SeedAsync(customerConnection, businessConnection);

            Assert.False(first.AlreadySeeded);
            Assert.True(second.AlreadySeeded);
            Assert.Equal(5, first.CompanyCount);
            Assert.Equal(8, first.CustomerCount);
            Assert.Equal(12, first.CustomerBookingCount);
            Assert.Equal(12, first.BusinessReservationCount);
            Assert.Equal(first.CustomerBookingCount, first.BusinessReservationCount);

            await AssertOfferingMetadataParityAsync(customerConnection, businessConnection);
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
        }
        finally
        {
            await DropDatabaseAsync(customerDatabase);
            await DropDatabaseAsync(businessDatabase);
        }
    }

    private static string Connection(string database) =>
        $"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

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
