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
            var second = await DemoDatabaseSeeder.SeedAsync(customerConnection, businessConnection);

            Assert.False(first.AlreadySeeded);
            Assert.True(second.AlreadySeeded);
            Assert.Equal(3, first.CompanyCount);
            Assert.Equal(5, first.CustomerCount);
            Assert.Equal(8, first.CustomerBookingCount);
            Assert.Equal(8, first.BusinessReservationCount);
            Assert.Equal(first.CustomerBookingCount, first.BusinessReservationCount);
        }
        finally
        {
            await DropDatabaseAsync(customerDatabase);
            await DropDatabaseAsync(businessDatabase);
        }
    }

    private static string Connection(string database) =>
        $"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

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
