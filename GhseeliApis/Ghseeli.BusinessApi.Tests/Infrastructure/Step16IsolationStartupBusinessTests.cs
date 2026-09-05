using System.Data.Common;
using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Expected-red isolation and startup contracts for the Business host.
/// </summary>
public sealed class Step16IsolationStartupBusinessTests
{
    private static readonly string[] RetainedControllers =
    [
        "AvailabilityController", "BookingStatusOutboxAdminController",
        "BusinessAuthController", "CatalogController", "CompanyProfileController",
        "InternalAppointmentsController", "InternalCatalogController",
        "InternalReservationsController", "WorkOrdersController"
    ];

    private static readonly string[] ForbiddenTables =
    [
        "UserAddresses", "Vehicles", "CustomerDevices", "CustomerConfigurations",
        "CatalogProviders", "CatalogBranches", "CatalogCategories", "CatalogOfferings",
        "CheckoutDrafts", "CheckoutDraftItems", "CheckoutDraftSelections",
        "CustomerBookings", "CustomerPayments", "StripeWebhookEvents",
        "ProcessedBookingStatusMessages", "Wallets", "Notifications"
    ];

    [Fact]
    public void BusinessAssemblyAndContainer_DoNotLoadOrResolveCustomerImplementation()
    {
        typeof(Program).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().NotContain("Ghseeli.CustomerApi");

        using var factory = new Step16BusinessFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetServices<object>()
            .Select(service => service.GetType().Assembly.GetName().Name)
            .Should().NotContain("Ghseeli.CustomerApi");
    }

    [Fact]
    public void ProductionDiGraph_ResolvesExactlyRetainedControllers()
    {
        using var factory = new Step16BusinessFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .OrderBy(type => type.Name)
            .ToArray();

        controllers.Select(type => type.Name).Should().Equal(RetainedControllers.Order());
        foreach (var controller in controllers)
        {
            ActivatorUtilities.CreateInstance(scope.ServiceProvider, controller)
                .Should().NotBeNull();
        }
    }

    [Fact]
    public void BusinessModelAndGeneratedSql_ContainOnlyBusinessOwnedTables()
    {
        var interceptor = new CapturingCommandInterceptor();
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlServer("Server=step16.invalid;Database=Step16Business;User Id=x;Password=y;TrustServerCertificate=True")
            .AddInterceptors(interceptor)
            .Options;
        using var context = new BusinessDbContext(options);
        var tables = context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => name is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        tables.Should().NotContain(ForbiddenTables);
        var sql = string.Join(";", tables.Select(name => $"SELECT TOP(0) * FROM [{name}]"));
        var act = () => context.Database.ExecuteSqlRaw(sql);
        act.Should().Throw<Step16CommandCapturedException>();
        interceptor.Commands.Should().ContainSingle()
            .Which.Should().NotContainAny(ForbiddenTables);
    }

    [Fact]
    public void BusinessConnectionAndSchemaIdentity_AreExplicitAndIndependent()
    {
        var program = File.ReadAllText(Path.Combine(
            FindSolutionRoot(), "Ghseeli.BusinessApi", "Program.cs"));
        program.Should().Contain("BusinessConnection");
        program.Should().NotContain("CustomerConnection");
        program.Should().Contain("BusinessSchema");
        program.Should().Contain("ValidateOnStart");
        program.Should().NotContain("EnsureCreated")
            .And.NotContain("Database.Migrate");
    }

    [Fact]
    public void BusinessRoles_AreExactlyOwnerEmployeeAndAdmin()
    {
        BusinessRoles.All.Should().Equal("Owner", "Employee", "Admin");
        BusinessRoles.All.Should().NotContain(["User", "Company"]);
    }

    [Fact]
    public async Task HealthSwaggerAndStartStop_WorkWithOnlyBusinessHost()
    {
        await using var factory = new Step16BusinessFactory();
        using var client = factory.CreateClient();

        using var health = await client.GetAsync("/api/health");
        health.IsSuccessStatusCode.Should().BeTrue(
            "health response was {0}", await health.Content.ReadAsStringAsync());
        (await client.GetAsync("/swagger/v1/swagger.json")).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public void Startup_HasFailFastSafeValidationForConnectionJwtHmacAndSchemaMismatch()
    {
        var program = File.ReadAllText(Path.Combine(
            FindSolutionRoot(), "Ghseeli.BusinessApi", "Program.cs"));
        program.Should().ContainAll(
            "BusinessConnection",
            "BusinessJwtSettings",
            "InternalServiceAuthentication",
            "BusinessSchema");
        program.Should().Contain("ValidateOnStart");
        program.Should().NotContain("The API will continue running");
    }

    [Fact]
    public void Migrations_AreBusinessIdentifiedAndSupportIdempotentRecreation()
    {
        var root = FindSolutionRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "Ghseeli.BusinessApi", "Persistence", "Migrations"),
            "*.cs");
        files.Should().NotBeEmpty();
        files.Select(Path.GetFileName)
            .Should().OnlyContain(name =>
                name!.Contains("Business", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("ModelSnapshot", StringComparison.OrdinalIgnoreCase));

        File.ReadAllText(Path.Combine(root, "Ghseeli.BusinessApi", "Program.cs"))
            .Should().NotContain("EnsureDeleted")
            .And.NotContain("EnsureCreated");
    }

    [Fact]
    public void EndpointMetadata_HasNoCustomerRouteOrAuthenticationLeakage()
    {
        using var factory = new Step16BusinessFactory();
        _ = factory.CreateClient();
        var routes = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? "")
            .ToArray();

        routes.Should().NotContain(route =>
            route.StartsWith("api/Auth", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("api/Users", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("api/Health/", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("api/v1/devices", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("api/v1/checkout", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("api/v1/payments", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("api/stripe", StringComparison.OrdinalIgnoreCase));
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

    private sealed class Step16BusinessFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"Step16Business-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:BusinessConnection", "unused-step16-business");
            builder.UseSetting("BusinessJwtSettings:SecretKey",
                "Step16BusinessJwtSecretKey_Minimum32Characters");
            builder.UseSetting("BusinessJwtSettings:Issuer", "Step16.Business");
            builder.UseSetting("BusinessJwtSettings:Audience", "Step16.Business.Clients");
            builder.UseSetting("CustomerBookingStatusClient:BaseUrl", "https://customer.absent");
            builder.UseSetting("CustomerBookingStatusClient:ServiceId", "step16-business");
            builder.UseSetting("CustomerBookingStatusClient:ActiveSecret",
                "Step16BusinessCallbackSecret_Minimum32Characters");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<BusinessDbContext>();
                services.RemoveAll<DbContextOptions<BusinessDbContext>>();
                services.AddDbContext<BusinessDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName));
            });
        }
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
