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

    [Fact]
    public void ProductionWorkflow_InjectsCustomerSmtpFromSecrets()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "deploy-monsterasp.yml"));

        workflow.Should().Contain(
            "CUSTOMER_SMTP_USERNAME: ${{ secrets.CUSTOMER_SMTP_USERNAME }}");
        workflow.Should().Contain(
            "CUSTOMER_SMTP_PASSWORD: ${{ secrets.CUSTOMER_SMTP_PASSWORD }}");
        workflow.Should().Contain(
            "CUSTOMER_SMTP_FROM_ADDRESS: ${{ secrets.CUSTOMER_SMTP_FROM_ADDRESS }}");
        workflow.Should().Contain(
            "\"CustomerSmtp__Enabled\" = \"true\"");
        workflow.Should().Contain(
            "\"CustomerSmtp__Host\" = \"smtp.gmail.com\"");
        workflow.Should().Contain(
            "\"CustomerSmtp__Port\" = \"587\"");
        workflow.Should().Contain(
            "\"CustomerSmtp__EnableSsl\" = \"true\"");
        workflow.Should().Contain(
            "\"CustomerSmtp__UserName\" = $env:CUSTOMER_SMTP_USERNAME");
        workflow.Should().Contain(
            "\"CustomerSmtp__Password\" = $env:CUSTOMER_SMTP_PASSWORD");
        workflow.Should().Contain(
            "\"CustomerSmtp__FromAddress\" = $env:CUSTOMER_SMTP_FROM_ADDRESS");
        workflow.Should().Contain(
            "\"CustomerSmtp__FromName\" = \"Ghseeli\"");
    }

    [Fact]
    public void ProductionSmoke_DecodesBinaryHttpContentBeforeParsingJson()
    {
        var smokeScript = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "GhseeliApis",
            "scripts",
            "http-tests",
            "Invoke-Step21ProductionSmoke.ps1"));

        smokeScript.Should().Contain(
            "if ($Response.Content -is [byte[]])");
        smokeScript.Should().Contain(
            "[Text.Encoding]::UTF8.GetString($Response.Content)");
        smokeScript.Should().Contain(
            "$problem = Read-ResponseText $webhook | ConvertFrom-Json");
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
