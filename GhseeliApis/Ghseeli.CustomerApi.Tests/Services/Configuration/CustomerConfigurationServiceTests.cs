using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Http;
using Moq;

namespace GhseeliApis.Tests.Services.Configuration;

/// <summary>
/// Defines active customer configuration retrieval behavior.
/// </summary>
public class CustomerConfigurationServiceTests
{
    private readonly Mock<ICustomerConfigurationRepository> _repository = new();
    private readonly Mock<IAppLogger> _logger = new();

    [Fact]
    public async Task GetActiveAsync_WhenHebrewIsSelected_LocalizesAndFallsBackToArabic()
    {
        var configuration = CreateConfiguration();
        _repository.Setup(repository => repository.GetActiveAsync(default))
            .ReturnsAsync(configuration);
        var service = CreateService();

        var response = await service.GetActiveAsync("he", "ar", default);

        response.Language.Should().Be(ConfigurationLanguageResolver.Hebrew);
        response.Display.Name.Should().Be(configuration.DisplayNameAr);
        response.Legal.Notice.Should().Be(configuration.LegalNoticeHe);
        response.Maintenance.Message.Should().Be(configuration.MaintenanceMessageAr);
    }

    [Fact]
    public async Task GetActiveAsync_WhenHeaderIsUnsupported_DefaultsToArabic()
    {
        var configuration = CreateConfiguration();
        _repository.Setup(repository => repository.GetActiveAsync(default))
            .ReturnsAsync(configuration);
        var service = CreateService();

        var response = await service.GetActiveAsync(null, "en-US,en;q=0.8", default);

        response.Language.Should().Be(ConfigurationLanguageResolver.Arabic);
        response.Display.Name.Should().Be(configuration.DisplayNameAr);
        response.Legal.Notice.Should().Be(configuration.LegalNoticeAr);
    }

    [Fact]
    public async Task GetActiveAsync_WhenConfigurationIsMissing_ThrowsStableUnavailableError()
    {
        _repository.Setup(repository => repository.GetActiveAsync(default))
            .ReturnsAsync((CustomerConfiguration?)null);
        var service = CreateService();

        var action = () => service.GetActiveAsync(null, null, default);

        await action.Should().ThrowAsync<CustomerConfigurationException>()
            .Where(exception =>
                exception.Code == ConfigurationProblemCodes.Unavailable &&
                exception.StatusCode == StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task GetActiveAsync_LogsWithoutSensitiveConfigurationValues()
    {
        var configuration = CreateConfiguration();
        _repository.Setup(repository => repository.GetActiveAsync(default))
            .ReturnsAsync(configuration);
        var service = CreateService();

        await service.GetActiveAsync("he", null, default);

        _logger.Verify(logger => logger.LogInfo(It.Is<string>(message =>
            message.Contains(configuration.Id.ToString(), StringComparison.Ordinal) &&
            message.Contains(ConfigurationLanguageResolver.Hebrew, StringComparison.Ordinal) &&
            !message.Contains(configuration.SupportEmail, StringComparison.Ordinal) &&
            !message.Contains(configuration.SupportPhone, StringComparison.Ordinal) &&
            !message.Contains(configuration.LegalNoticeAr, StringComparison.Ordinal))), Times.Once);
    }

    private CustomerConfigurationService CreateService() =>
        new(_repository.Object, _logger.Object);

    private static CustomerConfiguration CreateConfiguration() =>
        new()
        {
            Id = Guid.NewGuid(),
            IsActive = true,
            SupportEmail = "support@ghseeli.example",
            SupportPhone = "+00000000000",
            DisplayNameAr = "غسيلي",
            DisplayNameHe = null,
            LegalNoticeAr = "باستخدام التطبيق فإنك توافق على الشروط الحالية.",
            LegalNoticeHe = "השימוש באפליקציה כפוף לתנאים הנוכחיים.",
            PrivacyPolicyUrl = "https://ghseeli.example/privacy",
            TermsOfServiceUrl = "https://ghseeli.example/terms",
            IsMaintenanceModeEnabled = true,
            MaintenanceMessageAr = "الصيانة المجدولة جارية حالياً.",
            MaintenanceMessageHe = null
        };
}
