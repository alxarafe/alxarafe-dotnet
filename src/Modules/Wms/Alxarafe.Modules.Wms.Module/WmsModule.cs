using Alxarafe.AspNetCore;
using Alxarafe.Core;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Http;
using Alxarafe.Modules.Wms.Infrastructure;
using Alxarafe.Security;
using Alxarafe.Security.AspNetCore;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Alxarafe.Modules.Wms;

public sealed class WmsModule : IAlxarafeModule, IHttpModule, ICapabilityProvider, IReadPrimitiveProvider
{
    public const string Id = "erbas.wms";
    public const string DevelopmentUserEmailConfiguration = "Wms:DevelopmentUser:Email";
    public const string DevelopmentUserPasswordConfiguration = "Wms:DevelopmentUser:Password";
    public const string DefaultDevelopmentUserEmail = "user@example.test";
    public ModuleDescriptor Descriptor { get; } = new(Id, ModuleApi.CurrentVersion, []);

    public IEnumerable<ReadPrimitiveDescriptor> GetReadPrimitives() => WmsReadPrimitives.All;
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
        services.AddSingleton<IDbContextFactory<WmsDbContext>, TenantWmsDbContextFactory>();
        InventoryPermissions.Configure(services);
        services.AddOptions<DevelopmentUsersOptions>().Configure<IConfiguration, IHostEnvironment>((options, configuration, environment) =>
        {
            if (!environment.IsDevelopment()) return;
            string? password = configuration[DevelopmentUserPasswordConfiguration];
            if (string.IsNullOrEmpty(password)) return;
            string? email = configuration[DevelopmentUserEmailConfiguration];
            options.Users.Add(new DevelopmentUser(string.IsNullOrEmpty(email) ? DefaultDevelopmentUserEmail : email, password,
                [InventoryPermissions.WarehousesRead, InventoryPermissions.LocationsRead, InventoryPermissions.ItemsRead, InventoryPermissions.InventoryRead]));
        });
        services.AddSingleton<IReadPrimitiveExecutor, WmsReadPrimitiveExecutor>();
        services.AddSingleton<IInventoryStore, EfInventoryStore>();
        services.AddSingleton<InventoryService>();
        services.AddSingleton<IUomStore, EfUomStore>();
        services.AddSingleton<UomCatalogue>();
        services.AddSingleton<IStorageAttributeStore, EfStorageAttributeStore>();
        services.AddSingleton<StorageAttributeCatalogue>();
        services.AddSingleton<IItemFamilyStore, EfItemFamilyStore>();
        services.AddSingleton<ItemFamilyCatalogue>();
        services.AddSingleton<IItemFamilyAssociationStore, EfItemFamilyAssociationStore>();
        services.AddSingleton<ItemFamilyAssociations>();
        services.AddSingleton<ICapabilityExecutor, WmsCapabilityExecutor>();
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await services.GetRequiredService<ITenantDatabaseMigrationRunner>().MigrateAsync(async (connection, ct) =>
        {
            var options = new DbContextOptionsBuilder<WmsDbContext>();
            WmsStorage.Configure(options, connection, testing: false);
            await using var context = new WmsDbContext(options.Options);
            // Fail if the database does not exist; EF must not create a production database.
            await context.Database.OpenConnectionAsync(ct);
            await context.Database.MigrateAsync(ct);
        }, cancellationToken);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        InventoryEndpoints.Map(endpoints);
        UomEndpoints.Map(endpoints);
        StorageAttributeEndpoints.Map(endpoints);
        ItemFamilyEndpoints.Map(endpoints);
        ItemFamilyAssociationEndpoints.Map(endpoints);
    }
}
