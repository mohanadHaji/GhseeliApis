using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Configuration;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Validators.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Tests the customer configuration HTTP endpoint contract.
/// </summary>
public class ConfigurationControllerTests
{
    private readonly Mock<ICustomerConfigurationService> _service = new();
    private readonly Mock<IAppLogger> _logger = new();

    [Fact]
    public async Task Get_WhenConfigurationExists_ReturnsOk()
    {
        var expected = new ConfigurationResponse
        {
            Language = ConfigurationLanguageResolver.Hebrew,
            Support = new SupportContactResponse
            {
                Email = "support@ghseeli.example",
                Phone = "+00000000000"
            },
            Display = new DisplayConfigurationResponse
            {
                Name = "غسيلي"
            },
            Legal = new LegalConfigurationResponse
            {
                Notice = "השימוש באפליקציה כפוף לתנאים הנוכחיים.",
                PrivacyPolicyUrl = "https://ghseeli.example/privacy",
                TermsOfServiceUrl = "https://ghseeli.example/terms"
            },
            Maintenance = new MaintenanceConfigurationResponse
            {
                IsEnabled = false
            }
        };
        _service.Setup(service => service.GetActiveAsync("he", "ar", default))
            .ReturnsAsync(expected);
        var controller = CreateController();

        var result = await controller.Get("he", "ar", default);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Get_WhenLanguageOverrideIsInvalid_ReturnsStableProblemDetails()
    {
        var controller = CreateController();

        var result = await controller.Get("en", "he", default);

        var badRequest = result.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(ConfigurationProblemCodes.LanguageInvalid);
        problem.Extensions["language"].Should().Be(ConfigurationLanguageResolver.Hebrew);
        problem.Extensions["correlationId"].Should().Be("corr-step8-controller");
        var fieldErrors = problem.Extensions["fieldErrors"]
            .Should()
            .BeOfType<Dictionary<string, string[]>>()
            .Subject;
        fieldErrors.Should().ContainKey("language");
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_WhenLanguageOverrideIsMalformed_UsesSupportedLanguageFromMalformedHeader()
    {
        var controller = CreateController();

        var result = await controller.Get("-", "-, ;q=1, he-IL;q=0.8", default);

        var badRequest = result.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(ConfigurationProblemCodes.LanguageInvalid);
        problem.Extensions["language"].Should().Be(ConfigurationLanguageResolver.Hebrew);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_WhenConfigurationIsUnavailable_ReturnsServiceUnavailableProblem()
    {
        _service.Setup(service => service.GetActiveAsync(null, "ar", default))
            .ThrowsAsync(new CustomerConfigurationException(
                ConfigurationProblemCodes.Unavailable,
                StatusCodes.Status503ServiceUnavailable,
                "Unavailable."));
        var controller = CreateController();

        var result = await controller.Get(null, "ar", default);

        var unavailable = result.Should().BeOfType<ObjectResult>().Subject;
        unavailable.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        var problem = unavailable.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(ConfigurationProblemCodes.Unavailable);
        problem.Extensions["language"].Should().Be(ConfigurationLanguageResolver.Arabic);
    }

    private ConfigurationController CreateController()
    {
        return new ConfigurationController(
            _service.Object,
            new GetConfigurationRequestValidator(),
            _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "corr-step8-controller"
                }
            }
        };
    }
}
