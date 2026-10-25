using Alxarafe.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Alxarafe.Host.IntegrationTests;

// Privileged fixture arrangement outside HTTP explicitly targets the synthetic DB.
// Every HTTP request delegates to the real membership-based production resolver.
public sealed class TenantApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureServices(services =>
        services.AddSingleton<ITenantDatabaseResolver, FixtureDatabaseResolver>());

    private sealed class FixtureDatabaseResolver(IHttpContextAccessor http, TenantDatabaseResolver production, IConfiguration configuration) : ITenantDatabaseResolver
    {
        public Task<string> ResolveAsync(CancellationToken cancellationToken = default) => http.HttpContext is not null
            ? production.ResolveAsync(cancellationToken)
            : Task.FromResult(configuration.GetConnectionString(TenantDatabaseResolver.ConnectionPrefix + DevelopmentTenantBootstrap.DatabaseReference)
                ?? throw new InvalidOperationException("Explicit fixture business database is required."));
    }
}
