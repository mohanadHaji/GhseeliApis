using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace GhseeliApis.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class LahzaEndpointAttribute : Attribute;

public sealed class LahzaEndpointConvention(bool endpointsEnabled) :
    IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        if (endpointsEnabled)
        {
            return;
        }

        for (var controllerIndex = application.Controllers.Count - 1;
             controllerIndex >= 0;
             controllerIndex--)
        {
            var controller = application.Controllers[controllerIndex];
            if (controller.Attributes.OfType<LahzaEndpointAttribute>().Any())
            {
                application.Controllers.RemoveAt(controllerIndex);
                continue;
            }

            for (var actionIndex = controller.Actions.Count - 1;
                 actionIndex >= 0;
                 actionIndex--)
            {
                if (controller.Actions[actionIndex].Attributes
                    .OfType<LahzaEndpointAttribute>()
                    .Any())
                {
                    controller.Actions.RemoveAt(actionIndex);
                }
            }
        }
    }
}
