using FluentAssertions;
using Ghseeli.IntegrationContracts;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Protects the compile-time boundary between the independently deployed APIs.
/// </summary>
public class ProjectIsolationTests
{
    [Fact]
    public void BusinessApi_DoesNotReferenceCustomerApiImplementation()
    {
        var referencedAssemblies = typeof(Program).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name);

        referencedAssemblies.Should().NotContain("Ghseeli.CustomerApi");
    }

    [Fact]
    public void IntegrationContracts_DoesNotReferenceApplicationFrameworks()
    {
        var referencedAssemblies = typeof(ContractAssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Where(name => name != null)
            .ToList();

        referencedAssemblies.Should().NotContain(name =>
            name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal) ||
            name == "Ghseeli.CustomerApi" ||
            name == "Ghseeli.BusinessApi");
    }
}
