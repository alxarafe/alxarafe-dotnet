using Alxarafe.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Alxarafe.AspNetCore;

public interface IHttpModule
{
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

public static class ModuleEndpointExtensions
{
    public static IEndpointRouteBuilder MapAlxarafeModuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var runtime = endpoints.ServiceProvider.GetRequiredService<ModuleRuntime>();
        foreach (var module in runtime.Modules.OfType<IHttpModule>()) module.MapEndpoints(endpoints);
        return endpoints;
    }
}
