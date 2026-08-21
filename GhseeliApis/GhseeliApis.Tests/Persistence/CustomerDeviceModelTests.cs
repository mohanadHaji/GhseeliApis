using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Persistence;

/// <summary>
/// Verifies the secure relational shape of customer device registrations.
/// </summary>
public class CustomerDeviceModelTests
{
    [Fact]
    public void Model_UsesUniqueInstallationAndTokenHashIndexes()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new ApplicationDbContext(options);
        var entity = context.Model.FindEntityType(typeof(CustomerDevice));

        entity.Should().NotBeNull();
        entity!.GetIndexes().Single(index =>
                index.Properties.Single().Name == nameof(CustomerDevice.InstallationId))
            .IsUnique.Should().BeTrue();
        entity.GetIndexes().Single(index =>
                index.Properties.Single().Name == nameof(CustomerDevice.TokenHash))
            .IsUnique.Should().BeTrue();
        entity.FindProperty(nameof(CustomerDevice.RowVersion))!
            .IsConcurrencyToken.Should().BeTrue();
    }
}
