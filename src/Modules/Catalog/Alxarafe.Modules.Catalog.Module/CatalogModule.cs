using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Catalog.Application;
using Alxarafe.Modules.Catalog.Http;
using Alxarafe.Modules.Catalog.Infrastructure;
using Alxarafe.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Alxarafe.Modules.Catalog.ModuleDefinition;

public sealed class CatalogModule : IAlxarafeModule, IHttpModule
{
    public string Id => "Catalog";
    public IReadOnlyCollection<string> Dependencies => [];

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<CatalogDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("Catalog")));
        services.AddScoped<IItemRepository, EfItemRepository>();
        services.AddScoped<CreateItemHandler>();
        services.AddScoped<GetItemByIdHandler>();
        services.AddSingleton<IPermissionDefinitionProvider, CatalogPermissionDefinitionProvider>();
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public void MapEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints) => CatalogEndpoints.Map(endpoints);
}

public sealed class CatalogPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(CatalogPermissions.ItemsRead, "Read catalog items"),
        new(CatalogPermissions.ItemsCreate, "Create catalog items")
    ];
}
