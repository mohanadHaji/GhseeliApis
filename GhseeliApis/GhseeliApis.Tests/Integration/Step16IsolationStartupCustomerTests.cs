using System.Data.Common;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using GhseeliApis.Persistence;
using GhseeliApis.Tests.Integration;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GhseeliApis.Tests.Step16;

/// <summary>
/// Expected-red isolation and startup contracts for the Customer host.
/// </summary>
public sealed class Step16IsolationStartupCustomerTests
{
    private static readonly string[] RetainedControllers =
    [
        "AddressesController", "AuthController", "BookingConfirmationController",
        "CatalogController", "CheckoutDraftsController", "CheckoutPricingController",
        "ConfigurationController", "CustomerPaymentsController", "DevicesController",
        "HealthController", "InternalBookingStatusController", "PricingController",
        "LahzaWebhookController", "UsersController", "VehiclesController"
    ];

    private static readonly string[] ForbiddenTables =
    [
        "Companies", "CompanyAvailabilities", "Services", "ServiceOptions",
        "Bookings", "Payments", "Wallets", "WalletTransactions", "Notifications",
        "Branches", "ServiceCategories", "ServiceOfferings", "AddonGroups",
        "AddonChoices", "BusinessUserAssignments", "AppointmentReservations",
        "WorkOrders", "BookingStatusOutboxMessages"
    ];

    [Fact]
    public void ApiProjects_ReferenceOnlyNeutralSharedProjects()
    {
        var root = FindSolutionRoot();
        AssertNeutralReferences(Path.Combine(root, "GhseeliApis", "GhseeliApis.csproj"));
        AssertNeutralReferences(Path.Combine(root, "Ghseeli.BusinessApi", "Ghseeli.BusinessApi.csproj"));
    }

    [Fact]
    public void CustomerAssemblyAndContainer_DoNotLoadOrResolveBusinessImplementation()
    {
        typeof(Program).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().NotContain("Ghseeli.BusinessApi");

        using var factory = CreateFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetServices<object>()
            .Select(service => service.GetType().Assembly.GetName().Name)
            .Should().NotContain("Ghseeli.BusinessApi");
    }

    [Fact]
    public void ProductionDiGraph_ResolvesExactlyRetainedControllers()
    {
        using var factory = CreateFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .OrderBy(type => type.Name)
            .ToArray();

        controllers.Select(type => type.Name).Should().Equal(RetainedControllers.Order());
        foreach (var controller in controllers)
        {
            var instance = ActivatorUtilities.CreateInstance(scope.ServiceProvider, controller);
            instance.Should().NotBeNull();
        }
    }

    [Fact]
    public void CustomerModelAndGeneratedSql_ContainOnlyCustomerOwnedTables()
    {
        var interceptor = new CapturingCommandInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=step16.invalid;Database=Step16Customer;User Id=x;Password=y;TrustServerCertificate=True")
            .AddInterceptors(interceptor)
            .Options;
        using var context = new ApplicationDbContext(options);
        var tableNames = context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => name is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        tableNames.Should().NotContain(ForbiddenTables);
        var sql = string.Join(";", tableNames.Select(name => $"SELECT TOP(0) * FROM [{name}]"));
        var act = () => context.Database.ExecuteSqlRaw(sql);
        act.Should().Throw<Step16CommandCapturedException>();
        var referencedTables = Regex.Matches(
                interceptor.Commands.Should().ContainSingle().Which,
                @"\bFROM\s+\[(?<table>[^\]]+)\]",
                RegexOptions.IgnoreCase)
            .Select(match => match.Groups["table"].Value);
        referencedTables.Should().NotIntersectWith(ForbiddenTables);
    }

    [Fact]
    public void CustomerConnectionAndSchemaIdentity_AreExplicitAndIndependent()
    {
        var root = FindSolutionRoot();
        var program = File.ReadAllText(Path.Combine(root, "GhseeliApis", "Program.cs"));
        var setup = File.ReadAllText(Path.Combine(
            root, "GhseeliApis", "Extensions", "SqlServerSetupExtension.cs"));

        setup.Should().Contain("CustomerConnection");
        setup.Should().NotContain("BusinessConnection");
        program.Should().Contain("CustomerSchema");
        program.Should().Contain("ValidateOnStart");
        program.Should().NotContain("EnsureCreated");
        program.Should().NotContain("Database.Migrate");
    }

    [Fact]
    public void CustomerRoles_AreExactlyUserAndAdmin()
    {
        typeof(Program).Assembly.GetType("GhseeliApis.Constants.AppRoles")!
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(value => !value.Contains(','))
            .Should().BeEquivalentTo(["User", "Admin"]);

        File.ReadAllText(Path.Combine(FindSolutionRoot(), "GhseeliApis", "Program.cs"))
            .Should().NotContain("CompanyPolicy")
            .And.NotContain("\"Company\"");
    }

    [Fact]
    public async Task HealthSwaggerAndStartStop_WorkWithOnlyCustomerHost()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/Health")).IsSuccessStatusCode.Should().BeTrue();
        (await client.GetAsync("/api/Health/db")).IsSuccessStatusCode.Should().BeTrue();
        (await client.GetAsync("/swagger/v1/swagger.json")).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public void Startup_HasFailFastSafeValidationForConnectionJwtHmacAndSchemaMismatch()
    {
        var program = File.ReadAllText(Path.Combine(FindSolutionRoot(), "GhseeliApis", "Program.cs"));
        program.Should().ContainAll(
            "CustomerConnection",
            "JwtSettings",
            "CustomerInternalService",
            "CustomerSchema",
            "CheckDatabaseHealthAsync",
            "Skipping Customer role initialization",
            "Customer role initialization timed out",
            "Customer role initialization failed because the database became unavailable",
            "AllowUntrustedDevelopmentCertificate",
            "DangerousAcceptAnyServerCertificateValidator",
            "IsDevelopment");
        program.Should().Contain("ValidateOnStart");
        program.Should().Contain("CancelAfter");
        program.Should().NotContain("The API will continue running");
    }

    [Fact]
    public void Migrations_AreCustomerIdentifiedAndSupportIdempotentRecreation()
    {
        var root = FindSolutionRoot();
        var migrationFiles = Directory.GetFiles(
            Path.Combine(root, "GhseeliApis", "Migrations"), "*.cs");
        migrationFiles.Should().NotBeEmpty();
        migrationFiles.Select(Path.GetFileName)
            .Should().OnlyContain(name =>
                name!.Contains("Customer", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("ModelSnapshot", StringComparison.OrdinalIgnoreCase));

        File.ReadAllText(Path.Combine(root, "GhseeliApis", "Program.cs"))
            .Should().NotContain("EnsureDeleted")
            .And.NotContain("EnsureCreated");
    }

    [Fact]
    public void EndpointMetadata_HasNoRemovedOrCrossHostRouteLeakage()
    {
        using var factory = CreateFactory();
        _ = factory.CreateClient();
        var routes = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? "")
            .ToArray();

        routes.Should().NotContain(route =>
            route.StartsWith("api/Companies", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("api/Services", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("api/ServiceOptions", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("api/Bookings", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("api/v1/business", StringComparison.OrdinalIgnoreCase));
    }

    private static Step15CustomerApiFactory CreateFactory() =>
        new(
            CatalogTestSupport.CreateSnapshot(
                Guid.Parse("16161616-1616-1616-1616-161616161616"), 16),
            []);

    private static void AssertNeutralReferences(string projectFile)
    {
        var references = System.Xml.Linq.XDocument.Load(projectFile)
            .Descendants("ProjectReference")
            .Select(node => Path.GetFileNameWithoutExtension(
                node.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)))
            .ToArray();
        references.Should().OnlyContain(name =>
            name == "Ghseeli.Common" || name == "Ghseeli.IntegrationContracts");
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GhseeliApis.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the solution root.");
    }

    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override DbCommand CommandInitialized(
            CommandEndEventData eventData,
            DbCommand result)
        {
            Commands.Add(result.CommandText);
            throw new Step16CommandCapturedException();
        }
    }

    private sealed class Step16CommandCapturedException : Exception;
}
