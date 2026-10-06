using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Catalog.Application;
using Alxarafe.Modules.Catalog.Http;
using Alxarafe.Modules.Catalog.Infrastructure;
using Alxarafe.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Alxarafe.Modules.Catalog.ModuleDefinition;

public sealed class CatalogModule : IAlxarafeModule, IHttpModule
{
    public string Id => "Catalog";
    public IReadOnlyCollection<string> Dependencies => [];

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<CatalogDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("Catalog");
            if (provider.GetRequiredService<IHostEnvironment>().IsEnvironment("Testing") &&
                (string.IsNullOrWhiteSpace(connectionString) ||
                 !string.Equals(new NpgsqlConnectionStringBuilder(connectionString).Database, "alxarafe_test", StringComparison.Ordinal)))
                throw new InvalidOperationException("Testing requires the Catalog connection to use alxarafe_test.");
            options.UseNpgsql(connectionString);
        });
        services.AddExceptionHandler<CatalogExceptionHandler>();
        services.AddScoped<IItemRepository, EfItemRepository>();
        services.AddScoped<CreateItemHandler>();
        services.AddScoped<GetItemByIdHandler>();
        services.AddSingleton<IPermissionDefinitionProvider, CatalogPermissionDefinitionProvider>();
        services.AddOptions<DevelopmentUsersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            options.Users.Add(new DevelopmentUser("reader@example.test",
                configuration["SecuritySeed:ReaderPassword"] ?? "Reader_dev_only_123!", [CatalogPermissions.ItemsRead]));
            options.Users.Add(new DevelopmentUser("creator@example.test",
                configuration["SecuritySeed:CreatorPassword"] ?? "Creator_dev_only_123!", [CatalogPermissions.ItemsRead, CatalogPermissions.ItemsCreate]));
        });
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
