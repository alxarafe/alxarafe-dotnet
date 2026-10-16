using Alxarafe.Modularity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Alxarafe.AspNetCore;

public sealed record ModuleDisabledResponse() : ContractError(ErrorCode)
{
    public const string ErrorCode = "module_disabled";
}

/// <summary>Checks dynamic state once at the module HTTP boundary, after authorization.</summary>
public sealed class ModuleStateEndpointFilter(string moduleId) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var states = context.HttpContext.RequestServices.GetRequiredService<ModuleStateService>();
        try
        {
            await states.RequireEnabledAsync(moduleId, context.HttpContext.RequestAborted);
        }
        catch (ModuleDisabledException)
        {
            context.HttpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { NoStore = true };
            return Results.Json(new ModuleDisabledResponse(), statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        return await next(context);
    }
}
