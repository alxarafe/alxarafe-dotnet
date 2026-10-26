using Alxarafe.Core;
using Alxarafe.Modularity;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alxarafe.Modules.Assets;

public sealed class AssetsModule : IAlxarafeModule, IModuleServiceContributor, IModuleInitializer
{
    public const string Id = "erbas.assets";
    public const string Version = "1.0.0";
    public ModuleDescriptor Descriptor { get; } = new(Id, Version, ModuleApi.CurrentVersion, [], ModulePersistence.Tenant);
    public void ConfigureServices(IServiceCollection services)
    {
        AssetsPermissions.Configure(services);
        services.AddSingleton<IResourceDefinitionProvider, AssetsDefinitions>();
        services.AddSingleton<IDbContextFactory<AssetsDbContext>, TenantAssetsDbContextFactory>();
        services.AddScoped<IResourceRepository, AssetRepository>();
        services.AddScoped<IResourceRepository, AssetAssignmentRepository>();
        services.AddScoped<IResourceRepository, AssetFieldDefinitionRepository>();
        services.AddScoped<IResourceRepository, AssetFieldValueRepository>();
        services.AddScoped<IResourceRepository, AssetCatalogueRepository<AssetTypeRow>>();
        services.AddScoped<IResourceRepository, AssetCatalogueRepository<ManufacturerRow>>();
        services.AddScoped<IResourceRepository, AssetCatalogueRepository<AssetModelRow>>();
        services.AddScoped<IResourceRepository, AssetCatalogueRepository<AssetLocationRow>>();
        services.AddScoped<IResourceRepository, AssetCatalogueRepository<AssetStatusRow>>();
        foreach (string resource in AssetResources.All) services.AddSingleton(new ResourceRepositoryBinding(resource, ResourceRepositoryScope.Tenant));
        foreach (string action in new[] { AssetActions.Complete, AssetActions.Retire })
            services.AddScoped<IResourceActionHandler>(provider => new AssetActionHandler(action, provider.GetRequiredService<IDbContextFactory<AssetsDbContext>>()));
        foreach (string action in new[] { AssetActions.Assign, AssetActions.Unassign, AssetActions.Transfer })
            services.AddScoped<IResourceActionHandler>(provider => new AssetAssignmentActionHandler(action, provider.GetRequiredService<IDbContextFactory<AssetsDbContext>>()));
    }
    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await services.GetRequiredService<ITenantDatabaseMigrationRunner>().MigrateAsync(async (connection, ct) =>
        {
            var options = new DbContextOptionsBuilder<AssetsDbContext>();
            AssetsStorage.Configure(options, connection);
            await using var context = new AssetsDbContext(options.Options);
            await context.Database.OpenConnectionAsync(ct);
            await context.Database.MigrateAsync(ct);
            await AssetsSeed.InitializeAsync(context, ct);
        }, cancellationToken);
    }
}
