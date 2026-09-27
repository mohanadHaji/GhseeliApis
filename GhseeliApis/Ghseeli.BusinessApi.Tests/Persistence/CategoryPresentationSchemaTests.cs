using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ghseeli.BusinessApi.Tests.Persistence;

/// <summary>
/// Verifies the authoritative category presentation column contract.
/// </summary>
public sealed class CategoryPresentationSchemaTests
{
    [Fact]
    public void ServiceCategory_PresentationColumns_AreNullableAndBounded()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=CategoryPresentationSchema;Trusted_Connection=True")
            .Options;
        using var context = new BusinessDbContext(options);
        var entity = context.Model.FindEntityType(typeof(ServiceCategory))!;

        var imageUrl = entity.FindProperty(nameof(ServiceCategory.ImageUrl))!;
        imageUrl.IsNullable.Should().BeTrue();
        imageUrl.GetMaxLength().Should().Be(500);

        var colorHex = entity.FindProperty(nameof(ServiceCategory.ColorHex))!;
        colorHex.IsNullable.Should().BeTrue();
        colorHex.GetMaxLength().Should().Be(7);
    }
}
