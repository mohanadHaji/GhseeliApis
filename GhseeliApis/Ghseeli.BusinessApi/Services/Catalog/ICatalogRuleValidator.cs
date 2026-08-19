using Ghseeli.BusinessApi.Models;

namespace Ghseeli.BusinessApi.Services.Catalog;

public interface ICatalogRuleValidator
{
    void ValidateAddonGroup(AddonGroup addonGroup);
}
