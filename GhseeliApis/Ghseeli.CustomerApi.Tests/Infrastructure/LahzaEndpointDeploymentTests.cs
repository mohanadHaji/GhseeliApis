using FluentAssertions;

namespace GhseeliApis.Tests.Infrastructure;

/// <summary>
/// Protects the approved test-mode Lahza production deployment settings.
/// </summary>
public sealed class LahzaEndpointDeploymentTests
{
    [Fact]
    public void ProductionWorkflow_EnablesLahzaEndpointsForCustomerPoc()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "deploy-monsterasp.yml"));

        workflow.Should().Contain(
            "\"Lahza__EndpointsEnabled\" = \"true\"");
        workflow.Should().NotContain(
            "\"Lahza__EndpointsEnabled\" = \"false\"");
        workflow.Should().Contain(
            "LAHZA_SECRET_KEY: ${{ secrets.LAHZA_SECRET_KEY }}");
        workflow.Should().Contain(
            "throw \"Missing Production secret 'LAHZA_SECRET_KEY'.\"");
        workflow.Should().Contain(
            "Invoke-Step21ProductionSmoke.ps1");
        workflow.Should().Contain(
            "if: matrix.name == 'Customer'");
    }

    [Fact]
    public void ProductionWorkflow_UsesHttpsForRuntimeAndHealthProbes()
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
            "health_url: https://ghseelicustomer.runasp.net");
        workflow.Should().Contain(
            "health_url: https://ghseelibusiness.runasp.net");
        workflow.Should().Contain("\"${{ matrix.health_url }}\".TrimEnd('/')");
        workflow.Should().NotContain("health_url: http://");
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
