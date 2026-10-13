using Alxarafe.AspNetCore;
using Alxarafe.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Alxarafe.Host.IntegrationTests;

// HTTP probes exist only in the test assembly, never in the Host artifact.
internal sealed class RuntimeProbe : IStartupFilter, IPermissionDefinitionProvider
{
    public const string Path = "/test/runtime/math";
    public const string InvalidPath = "/test/runtime/invalid";
    public const string Permission = "runtime.probe.execute";

    public static WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(builder => builder
            .UseSetting(ConformanceModuleSource.EnabledConfiguration, "true")
            .ConfigureTestServices(services =>
            {
                services.AddSingleton<IStartupFilter, RuntimeProbe>();
                services.AddSingleton<IPermissionDefinitionProvider, RuntimeProbe>();
            }));

    public IEnumerable<PermissionDefinition> GetPermissions() => [new(Permission)];

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseRouting();
        next(app);
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGet(Path, () => Results.Ok(new { executed = true }))
                .RequireAuthorization(Permission)
                .AddEndpointFilter(new ModuleStateEndpointFilter(ConformanceModuleSource.MathId))
                .Produces<ModuleDisabledResponse>(StatusCodes.Status503ServiceUnavailable);
            endpoints.MapGet(InvalidPath, (Func<IResult>)(() =>
                throw new ArgumentException("Test-only structured request failure.")));
        });
    };
}
