using Ghseeli.BusinessApi.DTOs.Catalog;

namespace Ghseeli.BusinessApi.Services.Validation.Catalog;

public interface ICatalogRequestValidator
{
    void Validate(CreateServiceCategoryRequest request);
    void Validate(UpdateServiceCategoryRequest request);
    void Validate(CreateServiceOfferingRequest request);
    void Validate(UpdateServiceOfferingRequest request);
    void Validate(CreateAddonGroupRequest request);
    void Validate(UpdateAddonGroupRequest request);
    void Validate(CreateAddonChoiceRequest request);
    void Validate(UpdateAddonChoiceRequest request);
}
