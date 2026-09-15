using Microsoft.Data.SqlClient;

namespace Ghseeli.DemoData;

public static class DemoConnectionGuard
{
    public static void Validate(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A demo database connection string is required.");
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("The demo database connection string is invalid.", exception);
        }

        var server = builder.DataSource;
        var database = builder.InitialCatalog;
        var isLocalServer =
            server.Contains("(localdb)", StringComparison.OrdinalIgnoreCase) ||
            server.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            server.StartsWith("localhost\\", StringComparison.OrdinalIgnoreCase) ||
            server.Equals(".", StringComparison.Ordinal) ||
            server.StartsWith(".\\", StringComparison.Ordinal);
        var isDemoDatabase =
            database.Contains("Demo", StringComparison.OrdinalIgnoreCase) &&
            !database.Contains("Production", StringComparison.OrdinalIgnoreCase);

        if (!isLocalServer || !isDemoDatabase)
        {
            throw new InvalidOperationException(
                "Demo seeding is restricted to localhost/LocalDB databases whose name contains 'Demo' and not 'Production'.");
        }
    }
}
