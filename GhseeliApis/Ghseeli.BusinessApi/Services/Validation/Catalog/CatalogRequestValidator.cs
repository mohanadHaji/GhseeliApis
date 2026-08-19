using FluentValidation;
using Ghseeli.BusinessApi.DTOs.Catalog;

namespace Ghseeli.BusinessApi.Services.Validation.Catalog;

public class CatalogRequestValidator : ICatalogRequestValidator
{
    private readonly IValidator<CreateServiceCategoryRequest> _createCategoryValidator;
    private readonly IValidator<UpdateServiceCategoryRequest> _updateCategoryValidator;
    private readonly IValidator<CreateServiceOfferingRequest> _createOfferingValidator;
    private readonly IValidator<UpdateServiceOfferingRequest> _updateOfferingValidator;
    private readonly IValidator<CreateAddonGroupRequest> _createAddonGroupValidator;
    private readonly IValidator<UpdateAddonGroupRequest> _updateAddonGroupValidator;
    private readonly IValidator<CreateAddonChoiceRequest> _createAddonChoiceValidator;
    private readonly IValidator<UpdateAddonChoiceRequest> _updateAddonChoiceValidator;

    public CatalogRequestValidator(
        IValidator<CreateServiceCategoryRequest> createCategoryValidator,
        IValidator<UpdateServiceCategoryRequest> updateCategoryValidator,
        IValidator<CreateServiceOfferingRequest> createOfferingValidator,
        IValidator<UpdateServiceOfferingRequest> updateOfferingValidator,
        IValidator<CreateAddonGroupRequest> createAddonGroupValidator,
        IValidator<UpdateAddonGroupRequest> updateAddonGroupValidator,
        IValidator<CreateAddonChoiceRequest> createAddonChoiceValidator,
        IValidator<UpdateAddonChoiceRequest> updateAddonChoiceValidator)
    {
        _createCategoryValidator = createCategoryValidator;
        _updateCategoryValidator = updateCategoryValidator;
        _createOfferingValidator = createOfferingValidator;
        _updateOfferingValidator = updateOfferingValidator;
        _createAddonGroupValidator = createAddonGroupValidator;
        _updateAddonGroupValidator = updateAddonGroupValidator;
        _createAddonChoiceValidator = createAddonChoiceValidator;
        _updateAddonChoiceValidator = updateAddonChoiceValidator;
    }

    public void Validate(CreateServiceCategoryRequest request)
    {
        Validate(request, _createCategoryValidator);
    }

    public void Validate(UpdateServiceCategoryRequest request)
    {
        Validate(request, _updateCategoryValidator);
    }

    public void Validate(CreateServiceOfferingRequest request)
    {
        Validate(request, _createOfferingValidator);
    }

    public void Validate(UpdateServiceOfferingRequest request)
    {
        Validate(request, _updateOfferingValidator);
    }

    public void Validate(CreateAddonGroupRequest request)
    {
        Validate(request, _createAddonGroupValidator);
    }

    public void Validate(UpdateAddonGroupRequest request)
    {
        Validate(request, _updateAddonGroupValidator);
    }

    public void Validate(CreateAddonChoiceRequest request)
    {
        Validate(request, _createAddonChoiceValidator);
    }

    public void Validate(UpdateAddonChoiceRequest request)
    {
        Validate(request, _updateAddonChoiceValidator);
    }

    private static void Validate<T>(T request, IValidator<T> validator)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = validator.Validate(request);
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(error => ToCamelCase(error.PropertyName))
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        throw new CatalogValidationException(result.Errors[0].ErrorMessage, errors);
    }

    private static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return propertyName;
        }

        return char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
    }
}
