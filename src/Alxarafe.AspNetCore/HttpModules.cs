using System.Net.Mime;
using Alxarafe.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
        foreach (var module in runtime.Modules)
        {
            if (module is not IHttpModule httpModule) continue;
            var group = endpoints.MapGroup(string.Empty);
            group.AddEndpointFilter(new ModuleStateEndpointFilter(runtime.Registry.GetDescriptor(module).Id));
            group.WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status503ServiceUnavailable,
                typeof(ModuleDisabledResponse), [MediaTypeNames.Application.Json]));
            httpModule.MapEndpoints(group);
        }
        return endpoints;
    }
}
