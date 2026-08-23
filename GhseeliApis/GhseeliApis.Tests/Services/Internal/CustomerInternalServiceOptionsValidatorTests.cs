using FluentAssertions;
using GhseeliApis.Services.Internal;

namespace GhseeliApis.Tests.Services.Internal;

/// <summary>
/// Verifies safe Customer internal idempotency lease configuration.
/// </summary>
public sealed class CustomerInternalServiceOptionsValidatorTests
{
    [Fact]
    public void Validate_WhenLeaseCannotSupportHeartbeat_Fails()
    {
        var options = new CustomerInternalServiceOptions
        {
            InProgressRecoverySeconds = 0,
            InProgressLeaseRenewalFraction = 1
        };

        var result = new CustomerInternalServiceOptionsValidator()
            .Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("InProgressRecoverySeconds");
        result.FailureMessage.Should().Contain("InProgressLeaseRenewalFraction");
    }

    [Fact]
    public void Validate_WhenLeaseAndRenewalFractionAreSafe_Succeeds()
    {
        var options = new CustomerInternalServiceOptions
        {
            InProgressRecoverySeconds = 3,
            InProgressLeaseRenewalFraction = 0.25
        };

        new CustomerInternalServiceOptionsValidator()
            .Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenRenewalFractionIsNotFinite_Fails()
    {
        var options = new CustomerInternalServiceOptions
        {
            InProgressLeaseRenewalFraction = double.NaN
        };

        new CustomerInternalServiceOptionsValidator()
            .Validate(null, options).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData(double.Epsilon)]
    [InlineData(0.000_001)]
    [InlineData(0.099)]
    public void Validate_WhenComputedRenewalIntervalIsBelowPracticalMinimum_Fails(
        double fraction)
    {
        var options = new CustomerInternalServiceOptions
        {
            InProgressRecoverySeconds = 1,
            InProgressLeaseRenewalFraction = fraction,
            InProgressLeaseSafetyMarginMilliseconds = 100
        };

        var result = new CustomerInternalServiceOptionsValidator()
            .Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("computed renewal interval");
    }

    [Fact]
    public void Validate_WhenComputedRenewalIntervalRoundsToZero_Fails()
    {
        var options = new CustomerInternalServiceOptions
        {
            InProgressRecoverySeconds = 1,
            InProgressLeaseRenewalFraction = double.Epsilon
        };

        var result = new CustomerInternalServiceOptionsValidator()
            .Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.9, 100)]
    [InlineData(0.8, 200)]
    public void Validate_WhenRenewalIntervalReachesLeaseSafetyDeadline_Fails(
        double fraction,
        int safetyMarginMilliseconds)
    {
        var options = new CustomerInternalServiceOptions
        {
            InProgressRecoverySeconds = 1,
            InProgressLeaseRenewalFraction = fraction,
            InProgressLeaseSafetyMarginMilliseconds = safetyMarginMilliseconds
        };

        var result = new CustomerInternalServiceOptionsValidator()
            .Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("safety margin");
    }

    [Fact]
    public void Validate_WhenComputedRenewalIntervalIsExactlyMinimum_Succeeds()
    {
        var options = new CustomerInternalServiceOptions
        {
            InProgressRecoverySeconds = 1,
            InProgressLeaseRenewalFraction = 0.1,
            InProgressLeaseSafetyMarginMilliseconds = 100
        };

        new CustomerInternalServiceOptionsValidator()
            .Validate(null, options).Succeeded.Should().BeTrue();
    }
}
