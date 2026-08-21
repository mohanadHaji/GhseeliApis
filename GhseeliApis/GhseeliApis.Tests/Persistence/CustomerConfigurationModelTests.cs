using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Persistence;

/// <summary>
/// Verifies the persisted shape of customer application configuration.
/// </summary>
public class CustomerConfigurationModelTests
{
    [Fact]
    public void Model_UsesSingleActiveFilteredIndex_AndDoesNotSeedPlaceholderData()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        var entity = context.Model.FindEntityType(typeof(CustomerConfiguration));

        entity.Should().NotBeNull();
        var activeIndex = entity!.GetIndexes().Single(index =>
            index.Properties.Single().Name == nameof(CustomerConfiguration.IsActive));
        activeIndex.IsUnique.Should().BeTrue();
        activeIndex.GetFilter().Should().Be("[IsActive] = 1");
        context.CustomerConfigurations.Should().BeEmpty();
    }

    [Fact]
    public async Task Repository_GetActiveAsync_ReturnsTheActiveRecord()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new ApplicationDbContext(options);
        context.CustomerConfigurations.AddRange(
            new CustomerConfiguration
            {
                Id = Guid.NewGuid(),
                IsActive = false,
                SupportEmail = "inactive@ghseeli.example",
                SupportPhone = "+10000000000",
                DisplayNameAr = "غير نشط",
                LegalNoticeAr = "غير نشط",
                PrivacyPolicyUrl = "https://ghseeli.example/privacy-old",
                TermsOfServiceUrl = "https://ghseeli.example/terms-old"
            },
            new CustomerConfiguration
            {
                Id = Guid.NewGuid(),
                IsActive = true,
                SupportEmail = "active@ghseeli.example",
                SupportPhone = "+20000000000",
                DisplayNameAr = "نشط",
                LegalNoticeAr = "نشط",
                PrivacyPolicyUrl = "https://ghseeli.example/privacy",
                TermsOfServiceUrl = "https://ghseeli.example/terms"
            });
        await context.SaveChangesAsync();
        var repository = new CustomerConfigurationRepository(context);

        var active = await repository.GetActiveAsync(default);

        active.Should().NotBeNull();
        active!.SupportEmail.Should().Be("active@ghseeli.example");
    }
}
