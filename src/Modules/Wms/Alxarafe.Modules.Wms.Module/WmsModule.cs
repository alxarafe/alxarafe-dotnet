using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Security.AspNetCore;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Http;
using Alxarafe.Modules.Wms.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Alxarafe.Modules.Wms;

public sealed class WmsModule : IAlxarafeModule, IHttpModule, ICapabilityProvider
{
    public const string Id = "erbas.wms";
    public ModuleDescriptor Descriptor { get; } = new(Id, ModuleApi.CurrentVersion, []);

    public IEnumerable<CapabilityDescriptor> GetCapabilities() => WmsCapabilities.All;
    public IEnumerable<string> GetSchemaReferences() => WmsCapabilities.SchemaReferences;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(WmsCapabilities.UomsRead, CoreAdministration.Policy);
            options.AddPolicy(WmsCapabilities.StorageAttributesRead, CoreAdministration.Policy);
            options.AddPolicy(WmsCapabilities.ItemFamiliesRead, CoreAdministration.Policy);
        });
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
