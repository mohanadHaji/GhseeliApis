using Ghseeli.BusinessApi.DataPartitioning;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.DataPartitioning;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.DemoData.Tests;

/// <summary>
/// Verifies the shared-database Production/Demo isolation contract.
/// </summary>
public sealed class DemoPartitionContractTests
{
    [Fact]
    public void PublicContract_UsesOnlyStableProductionAndDemoValues()
    {
        Assert.Equal("Production", DataPartitionNames.Production);
        Assert.Equal("Demo", DataPartitionNames.Demo);
        Assert.Equal("ghseeli_data_partition", DataPartitionNames.ClaimType);
        Assert.Equal("dataPartition", DataPartitionNames.QueryParameter);
    }

    [Fact]
    public async Task CustomerContext_FiltersCatalogAndBookingRootsByPartition()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var production = new ApplicationDbContext(
                         options,
                         new CustomerDataPartitionContext()))
        {
            production.CatalogProviders.Add(new CatalogProviderReadModel
            {
                Id = Guid.NewGuid(),
                SourceCompanyId = Guid.NewGuid(),
                NameAr = "Production",
                CatalogVersion = 1
            });
            await production.SaveChangesAsync();
        }

        var demoPartition = new CustomerDataPartitionContext();
        demoPartition.SetTrustedPartition(DataPartitionNames.Demo);
        await using (var demo = new ApplicationDbContext(options, demoPartition))
        {
            demo.CatalogProviders.Add(new CatalogProviderReadModel
            {
                Id = Guid.NewGuid(),
                SourceCompanyId = Guid.NewGuid(),
                NameAr = "[DEMO]",
                CatalogVersion = 1
            });
            await demo.SaveChangesAsync();

            Assert.Single(await demo.CatalogProviders.ToListAsync());
            Assert.Equal("[DEMO]", (await demo.CatalogProviders.SingleAsync()).NameAr);
        }

        await using var verifyProduction = new ApplicationDbContext(
            options,
            new CustomerDataPartitionContext());
        Assert.Single(await verifyProduction.CatalogProviders.ToListAsync());
        Assert.Equal("Production", (await verifyProduction.CatalogProviders.SingleAsync()).NameAr);
    }

    [Fact]
    public async Task BusinessContext_FiltersCompaniesByPartition()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var production = new BusinessDbContext(
                         options,
                         new BusinessDataPartitionContext()))
        {
            production.Companies.Add(new Company { NameAr = "Production" });
            await production.SaveChangesAsync();
        }

        var demoPartition = new BusinessDataPartitionContext();
        demoPartition.SetTrustedPartition(DataPartitionNames.Demo);
        await using (var demo = new BusinessDbContext(options, demoPartition))
        {
            demo.Companies.Add(new Company { NameAr = "[DEMO]" });
            await demo.SaveChangesAsync();
            Assert.Single(await demo.Companies.ToListAsync());
            Assert.Equal("[DEMO]", (await demo.Companies.SingleAsync()).NameAr);
        }

        await using var verifyProduction = new BusinessDbContext(
            options,
            new BusinessDataPartitionContext());
        Assert.Single(await verifyProduction.Companies.ToListAsync());
        Assert.Equal("Production", (await verifyProduction.Companies.SingleAsync()).NameAr);
    }

    [Fact]
    public void PartitionContext_RejectsUnknownOrConflictingTrustedValues()
    {
        var context = new CustomerDataPartitionContext();
        context.SetTrustedPartition(DataPartitionNames.Demo);

        Assert.Throws<InvalidOperationException>(
            () => context.SetTrustedPartition(DataPartitionNames.Production));
        Assert.Throws<InvalidOperationException>(
            () => new CustomerDataPartitionContext().SetTrustedPartition("Test"));
    }
}
