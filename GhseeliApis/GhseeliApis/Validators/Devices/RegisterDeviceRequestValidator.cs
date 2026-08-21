using FluentValidation;
using GhseeliApis.DTOs.Devices;

namespace GhseeliApis.Validators.Devices;

public sealed class RegisterDeviceRequestValidator : AbstractValidator<RegisterDeviceRequest>
{
    private static readonly HashSet<string> SupportedPlatforms =
        new(StringComparer.OrdinalIgnoreCase) { "iOS", "Android" };

    public RegisterDeviceRequestValidator()
    {
        RuleFor(request => request.InstallationId)
            .NotEmpty();

        RuleFor(request => request.Platform)
            .NotEmpty()
            .Must(platform => SupportedPlatforms.Contains(platform))
            .WithMessage("Platform must be iOS or Android.");

        RuleFor(request => request.AppVersion)
            .MaximumLength(32)
            .Matches(@"^[A-Za-z0-9][A-Za-z0-9._+\-]*$")
            .When(request => request.AppVersion is not null)
            .WithMessage("AppVersion contains unsupported characters.");
    }
}
