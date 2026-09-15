using System.Text.Json;
using Ghseeli.DemoData;

namespace Ghseeli.DemoData.Tests;

/// <summary>
/// Verifies the frontend demo dataset contract and production-safety guard.
/// </summary>
public sealed class DemoDataDefinitionTests
{
    [Fact]
    public void Create_ReturnsModerateClearlyMarkedDataset()
    {
        var data = DemoDataDefinition.Create();

        Assert.Equal("demo", data.Metadata.DatasetType);
        Assert.False(data.Metadata.LocalDevelopmentOnly);
        Assert.Contains("DEMO", data.Metadata.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, data.Companies.Count);
        Assert.Equal(8, data.BusinessUsers.Count);
        Assert.Equal(8, data.Companies.Sum(company => company.Branches.Count));
        Assert.Equal(6, data.Companies.Sum(company => company.Categories.Count));
        Assert.Equal(20, data.Companies.Sum(company => company.Offerings.Count));
        Assert.Equal(10, data.Companies.Sum(company =>
            company.Offerings.Sum(offering => offering.AddonGroups.Count)));
        Assert.Equal(30, data.Companies.Sum(company =>
            company.Offerings.Sum(offering =>
                offering.AddonGroups.Sum(group => group.Choices.Count))));
        Assert.Equal(8, data.Customers.Count);
        Assert.Equal(10, data.Customers.Sum(customer => customer.Devices.Count));
        Assert.Equal(13, data.Customers.Sum(customer => customer.Vehicles.Count));
        Assert.Equal(12, data.Customers.Sum(customer => customer.Addresses.Count));
        Assert.Equal(6, data.Drafts.Count);
        Assert.Equal(12, data.Bookings.Count);
        Assert.Equal(8, data.Bookings.Count(booking => booking.Payment is not null));
    }

    [Fact]
    public void Create_UsesReservedFictionalIdentitiesAndVisibleDemoLabels()
    {
        var data = DemoDataDefinition.Create();

        Assert.All(data.Companies, company =>
        {
            Assert.StartsWith("[DEMO]", company.NameEn);
            Assert.Contains("تجريبي", company.NameAr, StringComparison.Ordinal);
        });
        Assert.All(data.Customers, customer =>
        {
            Assert.EndsWith("@example.test", customer.Email, StringComparison.Ordinal);
            Assert.StartsWith("[DEMO]", customer.FullName);
            Assert.StartsWith("+972555000", customer.Phone);
            Assert.Equal("Demo123!", customer.Password);
        });
        Assert.All(data.BusinessUsers, user =>
        {
            Assert.EndsWith("@example.test", user.Email, StringComparison.Ordinal);
            Assert.Equal("Demo123!", user.Password);
        });
        Assert.All(
            data.Bookings.Where(booking => booking.Payment is not null),
            booking => Assert.StartsWith("DEMO-", booking.Payment!.ProviderReference));
    }

    [Fact]
    public void Create_ProducesStableJson()
    {
        var first = DemoDataJson.Serialize(DemoDataDefinition.Create());
        var second = DemoDataJson.Serialize(DemoDataDefinition.Create());

        Assert.Equal(first, second);
        using var document = JsonDocument.Parse(first);
        Assert.Equal("demo", document.RootElement.GetProperty("metadata").GetProperty("datasetType").GetString());
    }

    [Fact]
    public void Create_HasResolvableCrossSystemReferences()
    {
        var data = DemoDataDefinition.Create();
        var companyIds = data.Companies.Select(company => company.Id).ToHashSet();
        var branches = data.Companies.SelectMany(company => company.Branches).ToDictionary(branch => branch.Id);
        var offerings = data.Companies.SelectMany(company => company.Offerings).ToDictionary(offering => offering.Id);
        var customers = data.Customers.ToDictionary(customer => customer.Id);
        var devices = data.Customers.SelectMany(customer => customer.Devices).ToDictionary(device => device.Id);

        Assert.All(data.Drafts, draft =>
        {
            Assert.Contains(draft.CompanyId, companyIds);
            Assert.Equal(draft.CompanyId, branches[draft.BranchId].CompanyId);
            Assert.Contains(draft.DeviceId, devices.Keys);
            Assert.All(draft.Items, item => Assert.Contains(item.OfferingId, offerings.Keys));
        });
        Assert.All(data.Bookings, booking =>
        {
            Assert.Contains(booking.CompanyId, companyIds);
            Assert.Equal(booking.CompanyId, branches[booking.BranchId].CompanyId);
            Assert.Contains(booking.CustomerId, customers.Keys);
            Assert.Contains(booking.DeviceId, devices.Keys);
            Assert.NotEqual(Guid.Empty, booking.CustomerReferenceId);
            Assert.NotEqual(Guid.Empty, booking.BusinessReservationId);
            Assert.NotEqual(Guid.Empty, booking.BusinessWorkOrderId);
            Assert.All(booking.Items, item => Assert.Contains(item.OfferingId, offerings.Keys));
        });
    }

    [Theory]
    [InlineData("Server=(localdb)\\MSSQLLocalDB;Database=GhseeliCustomer_FrontendDemo;Trusted_Connection=True;")]
    [InlineData("Server=localhost;Database=GhseeliBusiness_FrontendDemo;Integrated Security=True;TrustServerCertificate=True;")]
    public void ValidateConnection_AcceptsOnlyClearlyNamedLocalDemoDatabases(string connectionString)
    {
        DemoConnectionGuard.Validate(connectionString);
    }

    [Theory]
    [InlineData("Server=production.example.com;Database=GhseeliCustomer_FrontendDemo;User Id=app;Password=x;")]
    [InlineData("Server=(localdb)\\MSSQLLocalDB;Database=GhseeliCustomer;Trusted_Connection=True;")]
    [InlineData("Server=localhost;Database=ProductionDemo;Integrated Security=True;")]
    [InlineData("")]
    public void ValidateConnection_RejectsUnsafeDestinations(string connectionString)
    {
        Assert.Throws<InvalidOperationException>(() => DemoConnectionGuard.Validate(connectionString));
    }
}
