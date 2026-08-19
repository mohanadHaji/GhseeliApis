using FluentAssertions;
using Ghseeli.BusinessApi.DTOs.Auth;
using Ghseeli.BusinessApi.Validators.Auth;

namespace Ghseeli.BusinessApi.Tests.Validators.Auth;

/// <summary>
/// Verifies FluentValidation request rules for Business auth endpoints.
/// </summary>
public class BusinessAuthRequestValidatorsTests
{
    [Fact]
    public void RegisterOwnerRequestValidator_AllowsMissingHebrewCompanyName()
    {
        var validator = new RegisterOwnerRequestValidator();
        var request = new RegisterOwnerRequest
        {
            Email = "owner@example.com",
            Password = "Password1",
            FullName = "Owner Name",
            PhoneNumber = "0500000000",
            CompanyNameAr = "شركة غسيلي"
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void RegisterOwnerRequestValidator_RejectsMissingArabicCompanyName()
    {
        var validator = new RegisterOwnerRequestValidator();
        var request = new RegisterOwnerRequest
        {
            Email = "owner@example.com",
            Password = "Password1",
            FullName = "Owner Name",
            CompanyNameAr = " "
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(RegisterOwnerRequest.CompanyNameAr));
    }

    [Fact]
    public void RegisterOwnerRequestValidator_RejectsSuppliedHebrewCompanyNameLongerThanMaxLength()
    {
        var validator = new RegisterOwnerRequestValidator();
        var request = new RegisterOwnerRequest
        {
            Email = "owner@example.com",
            Password = "Password1",
            FullName = "Owner Name",
            CompanyNameAr = "شركة غسيلي",
            CompanyNameHe = new string('א', 201)
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(RegisterOwnerRequest.CompanyNameHe));
    }

    [Fact]
    public void BusinessLoginRequestValidator_RejectsInvalidEmail()
    {
        var validator = new BusinessLoginRequestValidator();
        var request = new BusinessLoginRequest
        {
            Email = "not-an-email",
            Password = "Password1"
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(BusinessLoginRequest.Email));
    }
}
