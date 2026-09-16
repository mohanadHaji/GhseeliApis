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

public static class HostedDemoConnectionGuard
{
    public const string RequiredConfirmation = "SEED HOSTED DEMO";
    private const string RequiredRepository = "mohanadHaji/GhseeliApis";
    private const string RequiredReference = "refs/heads/master";

    public static void Validate(
        string customerConnectionString,
        string businessConnectionString,
        string? repository,
        string? reference,
        string? githubActions,
        string? confirmation)
    {
        if (!string.Equals(repository, RequiredRepository, StringComparison.Ordinal) ||
            !string.Equals(reference, RequiredReference, StringComparison.Ordinal) ||
            !string.Equals(githubActions, "true", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(confirmation, RequiredConfirmation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Hosted Demo seeding requires the protected master-branch GitHub Actions workflow and exact confirmation.");
        }

        var customer = ParseRemoteTarget(customerConnectionString, "Customer");
        var business = ParseRemoteTarget(businessConnectionString, "Business");
        if (string.Equals(customer.DataSource, business.DataSource, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(customer.InitialCatalog, business.InitialCatalog, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Customer and Business hosted Demo targets must be different databases.");
        }
    }

    private static SqlConnectionStringBuilder ParseRemoteTarget(
        string connectionString,
        string name)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"{name} hosted database connection is required.");
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"{name} hosted database connection is invalid.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource) ||
            string.IsNullOrWhiteSpace(builder.InitialCatalog) ||
            IsLocalServer(builder.DataSource))
        {
            throw new InvalidOperationException(
                $"{name} hosted Demo seeding requires a named remote SQL database.");
        }

        return builder;
    }

    private static bool IsLocalServer(string server) =>
        server.Contains("(localdb)", StringComparison.OrdinalIgnoreCase) ||
        server.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        server.StartsWith("localhost\\", StringComparison.OrdinalIgnoreCase) ||
        server.Equals(".", StringComparison.Ordinal) ||
        server.StartsWith(".\\", StringComparison.Ordinal);
}
