using FluentAssertions;

namespace GhseeliApis.Tests.Infrastructure;

/// <summary>
/// Protects the production default that keeps Lahza routes hidden.
/// </summary>
public sealed class LahzaEndpointDeploymentTests
{
    [Fact]
    public void ProductionWorkflow_DisablesLahzaEndpoints()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "deploy-monsterasp.yml"));

        workflow.Should().Contain(
            "\"Lahza__EndpointsEnabled\" = \"false\"");
    }

    [Fact]
    public void ProductionWorkflow_UsesHttpOnlyForTemporaryHealthProbes()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "deploy-monsterasp.yml"));

        workflow.Should().Contain(
            "CUSTOMER_SITE_URL: https://ghseelicustomer.runasp.net");
        workflow.Should().Contain(
            "BUSINESS_SITE_URL: https://ghseelibusiness.runasp.net");
        workflow.Should().Contain(
            "health_url: http://ghseelicustomer.runasp.net");
        workflow.Should().Contain(
            "health_url: http://ghseelibusiness.runasp.net");
        workflow.Should().Contain("\"${{ matrix.health_url }}\".TrimEnd('/')");
        workflow.Should().NotContain("\"${{ matrix.site_url }}\".TrimEnd('/')");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".github")) &&
                Directory.Exists(Path.Combine(directory.FullName, "GhseeliApis")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }
}
