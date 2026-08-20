using FluentAssertions;
using Ghseeli.BusinessApi.DTOs.Companies;
using Ghseeli.BusinessApi.Validators.Companies;

namespace Ghseeli.BusinessApi.Tests.Validators.Companies;

/// <summary>
/// Verifies FluentValidation request rules for company profile and branch endpoints.
/// </summary>
public class CompanyRequestValidatorsTests
{
    [Fact]
    public void UpdateCompanyProfileRequestValidator_AllowsMissingHebrewName()
    {
        var validator = new UpdateCompanyProfileRequestValidator();
        var request = new UpdateCompanyProfileRequest
        {
            NameAr = "شركة غسيلي",
            Phone = "0500000000"
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateCompanyProfileRequestValidator_RejectsMissingArabicName()
    {
        var validator = new UpdateCompanyProfileRequestValidator();
        var request = new UpdateCompanyProfileRequest
        {
            NameAr = " "
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateCompanyProfileRequest.NameAr));
    }

    [Fact]
    public void CreateBranchRequestValidator_AllowsMissingHebrewNameAndAddress()
    {
        var validator = new CreateBranchRequestValidator();
        var request = new CreateBranchRequest
        {
            NameAr = "الفرع الرئيسي",
            AddressAr = "الرياض",
            Latitude = 24.7136,
            Longitude = 46.6753
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateBranchRequestValidator_RejectsSingleCoordinateWithoutItsPair()
    {
        var validator = new CreateBranchRequestValidator();
        var request = new CreateBranchRequest
        {
            NameAr = "الفرع الرئيسي",
            AddressAr = "الرياض",
            Latitude = 24.7136
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.ErrorMessage.Contains("together", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateBranchRequestValidator_AcceptsExactCoordinateExtremes()
    {
        var validator = new CreateBranchRequestValidator();
        var request = new CreateBranchRequest
        {
            NameAr = "الفرع الرئيسي",
            AddressAr = "الرياض",
            Latitude = -90d,
            Longitude = 180d
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateBranchRequestValidator_RejectsSuppliedHebrewAddressLongerThanMaxLength()
    {
        var validator = new CreateBranchRequestValidator();
        var request = new CreateBranchRequest
        {
            NameAr = "الفرع الرئيسي",
            AddressAr = "الرياض",
            AddressHe = new string('א', 301)
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateBranchRequest.AddressHe));
    }

    [Fact]
    public void UpdateBranchRequestValidator_RejectsOutOfRangeLatitude()
    {
        var validator = new UpdateBranchRequestValidator();
        var request = new UpdateBranchRequest
        {
            NameAr = "الفرع الرئيسي",
            AddressAr = "الرياض",
            Latitude = 91
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateBranchRequest.Latitude));
    }
}
