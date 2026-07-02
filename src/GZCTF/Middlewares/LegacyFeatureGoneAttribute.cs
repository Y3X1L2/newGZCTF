using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GZCTF.Middlewares;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class LegacyFeatureGoneAttribute(string message) : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        context.Result = new ObjectResult(new RequestResponse(message, StatusCodes.Status410Gone))
        {
            StatusCode = StatusCodes.Status410Gone
        };
    }
}
