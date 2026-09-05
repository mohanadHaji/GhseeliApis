using FluentValidation;
using GhseeliApis.DTOs.Configuration;
using GhseeliApis.Services.Configuration;

namespace GhseeliApis.Validators.Configuration;

public sealed class GetConfigurationRequestValidator : AbstractValidator<GetConfigurationRequest>
{
    public GetConfigurationRequestValidator()
    {
        RuleFor(request => request.Language)
            .Cascade(CascadeMode.Stop)
            .Must(language => ConfigurationLanguageResolver.TryNormalizeOverride(language, out _))
            .WithMessage("Language must be ar or he.")
            .WithErrorCode(ConfigurationProblemCodes.LanguageInvalid)
            .When(request => request.Language is not null);
    }
}
