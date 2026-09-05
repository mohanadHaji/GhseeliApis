using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using GhseeliApis.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GhseeliApis.Tests;

/// <summary>
/// Frozen Step 16 compiled/source boundary checks for removed Customer concerns.
/// </summary>
public sealed class Step16CustomerSchemaBoundaryTests
{
    private static readonly string[] RemovedConcernNames =
    [
        "Company", "CompanyAvailability", "Service", "ServiceOption", "Booking",
        "Payment", "Wallet", "WalletTransaction", "Notification"
    ];

    [Fact]
    public void Customer_assembly_has_no_removed_model_configuration_or_application_types()
    {
        var assembly = typeof(ApplicationDbContext).Assembly;
        var forbiddenFullNames = RemovedConcernNames.SelectMany(concern => new[]
        {
            $"GhseeliApis.Models.{concern}",
            $"GhseeliApis.Persistence.{concern}Configuration",
            $"GhseeliApis.Repositories.I{concern}Repository",
            $"GhseeliApis.Repositories.{concern}Repository",
            $"GhseeliApis.Repositories.Interfaces.I{concern}Repository",
            $"GhseeliApis.Handlers.I{concern}Handler",
            $"GhseeliApis.Handlers.{concern}Handler",
            $"GhseeliApis.Handlers.Interfaces.I{concern}Handler",
            $"GhseeliApis.Controllers.{Pluralize(concern)}Controller",
            $"GhseeliApis.Controllers.{concern}Controller"
        });

        assembly.GetTypes().Select(type => type.FullName)
            .Should().NotIntersectWith(forbiddenFullNames);

        var forbiddenDtoNames = new[]
        {
            "CreateCompanyRequest", "UpdateCompanyRequest", "CompanyResponse",
            "CreateCompanyAvailabilityRequest", "UpdateCompanyAvailabilityRequest",
            "CompanyAvailabilityResponse", "CreateServiceRequest", "UpdateServiceRequest",
            "ServiceResponse", "CreateServiceOptionRequest", "UpdateServiceOptionRequest",
            "ServiceOptionResponse", "CreateBookingRequest", "UpdateBookingRequest",
            "BookingResponse", "CreatePaymentRequest", "UpdatePaymentStatusRequest",
            "PaymentResponse", "WalletResponse", "WalletTransactionResponse",
            "NotificationResponse"
        };
        assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("GhseeliApis.DTOs", StringComparison.Ordinal) == true)
            .Select(type => type.Name)
            .Should().NotIntersectWith(forbiddenDtoNames);
    }

    [Fact]
    public void Customer_program_has_no_removed_di_policy_role_or_seed_reference()
    {
        var programText = File.ReadAllText(Path.Combine(FindCustomerProjectRoot(), "Program.cs"));
        var forbiddenPatterns = new[]
        {
            @"\bICompanyRepository\b", @"\bCompanyRepository\b", @"\bICompanyHandler\b", @"\bCompanyHandler\b",
            @"\bIServiceRepository\b", @"\bServiceRepository\b", @"\bIServiceHandler\b", @"\bServiceHandler\b",
            @"\bIServiceOptionRepository\b", @"\bServiceOptionRepository\b", @"\bIServiceOptionHandler\b", @"\bServiceOptionHandler\b",
            @"\bIBookingRepository\b", @"\bBookingRepository\b", @"\bIBookingHandler\b", @"\bBookingHandler\b",
            @"\bIPaymentRepository\b", @"\bPaymentRepository\b", @"\bIPaymentHandler\b", @"\bPaymentHandler\b",
            @"\bWallet(Transaction)?\b", @"\bNotification\b", @"AppRoles\.Company", @"""Company"""
        };

        forbiddenPatterns.Should().OnlyContain(pattern =>
            !Regex.IsMatch(programText, pattern, RegexOptions.CultureInvariant),
            "removed concerns must not be registered, authorized, or seeded by the Customer host");
    }

    [Fact]
    public void Customer_service_collection_cannot_resolve_removed_application_abstractions()
    {
        var assembly = typeof(ApplicationDbContext).Assembly;
        var removedTypes = assembly.GetTypes().Where(type =>
            RemovedConcernNames.Any(concern =>
                type.Name == concern ||
                type.Name == $"I{concern}Repository" || type.Name == $"{concern}Repository" ||
                type.Name == $"I{concern}Handler" || type.Name == $"{concern}Handler" ||
                type.Name == $"{concern}Controller" || type.Name == $"{Pluralize(concern)}Controller"));

        removedTypes.Should().BeEmpty();

        using var provider = new ServiceCollection().BuildServiceProvider();
        removedTypes.Should().OnlyContain(type => provider.GetService(type) == null);
    }

    [Fact]
    public void Customer_source_has_no_removed_model_or_migration_snapshot_nodes()
    {
        var projectRoot = FindCustomerProjectRoot();
        var modelFiles = Directory.GetFiles(Path.Combine(projectRoot, "Models"), "*.cs");
        var snapshot = Directory.GetFiles(Path.Combine(projectRoot, "Migrations"), "*ModelSnapshot.cs")
            .Should().ContainSingle().Which;

        modelFiles.Select(Path.GetFileNameWithoutExtension)
            .Should().NotIntersectWith(RemovedConcernNames);

        var snapshotText = File.ReadAllText(snapshot);
        foreach (var concern in RemovedConcernNames)
        {
            snapshotText.Should().NotMatchRegex($@"GhseeliApis\.Models\.{Regex.Escape(concern)}(?:\""|\b)");
        }
    }

    private static string FindCustomerProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "Ghseeli.CustomerApi",
                "Ghseeli.CustomerApi.csproj");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Customer API project root.");
    }

    private static string Pluralize(string value) => value switch
    {
        "Company" => "Companies",
        "CompanyAvailability" => "CompanyAvailabilities",
        _ => $"{value}s"
    };
}
