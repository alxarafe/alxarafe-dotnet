using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Http;
using Alxarafe.Modules.Wms.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Alxarafe.Modules.Wms;

public sealed class WmsModule : IAlxarafeModule, IHttpModule
{
    public const string Id = "erbas.wms";
    public ModuleDescriptor Descriptor { get; } = new(Id, ModuleApi.CurrentVersion, []);

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContextFactory<WmsDbContext>((provider, options) =>
            WmsStorage.Configure(options,
                provider.GetRequiredService<IConfiguration>().GetConnectionString(WmsStorage.ConnectionName),
                provider.GetRequiredService<IHostEnvironment>().IsEnvironment(WmsStorage.TestingEnvironment)));
        services.AddSingleton<IUomStore, EfUomStore>();
        services.AddSingleton<UomCatalogue>();
        services.AddSingleton<IStorageAttributeStore, EfStorageAttributeStore>();
        services.AddSingleton<StorageAttributeCatalogue>();
        services.AddSingleton<IItemFamilyStore, EfItemFamilyStore>();
        services.AddSingleton<ItemFamilyCatalogue>();
        services.AddSingleton<IItemFamilyAssociationStore, EfItemFamilyAssociationStore>();
        services.AddSingleton<ItemFamilyAssociations>();
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var context = await services.GetRequiredService<IDbContextFactory<WmsDbContext>>().CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        UomEndpoints.Map(endpoints);
        StorageAttributeEndpoints.Map(endpoints);
        ItemFamilyEndpoints.Map(endpoints);
        ItemFamilyAssociationEndpoints.Map(endpoints);
    }
}
