using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Npgsql;

namespace Alxarafe.Host;

// Stateless accessor: scoped TenantContext is obtained from the current HTTP scope,
// never captured by singleton business factories/stores.
public sealed class TenantDatabaseResolver(IHttpContextAccessor http, ITenantDirectory directory, IConfiguration configuration, IHostEnvironment environment)
    : ITenantDatabaseResolver, ITenantDatabaseMigrationRunner
{
    public const string ConnectionPrefix = "Tenant_";
    private const string TargetItem = "erbas.tenant.database_target";

    public async Task<string> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var scope = http.HttpContext?.RequestServices ?? throw new TenantAccessException(TenantAccessException.Required);
        var tenant = await scope.GetRequiredService<IRequestTenantContext>().RequireAsync(cancellationToken);
        var connection = await ConnectionAsync(tenant, cancellationToken);
        var target = Target(connection);
        var items = http.HttpContext!.Items;
        if (items.TryGetValue(TargetItem, out var previous) && !Equals(previous, target))
            throw new TenantAccessException(TenantAccessException.ContextChanged);
        items[TargetItem] = target;
        return connection;
    }

    public async Task MigrateAsync(Func<string, CancellationToken, Task> migrate, CancellationToken cancellationToken = default)
    {
        foreach (var tenant in (await directory.ListAsync(cancellationToken)).Where(tenant => tenant.Active))
            await migrate(await ConnectionAsync(tenant, cancellationToken), cancellationToken);
    }

    private async Task<string> ConnectionAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        if (!tenant.Active) throw new TenantAccessException(TenantAccessException.Inactive);
        string? connection = configuration.GetConnectionString(ConnectionPrefix + tenant.DatabaseReference);
        var target = Target(connection);
        if (target is null) throw Unavailable();
        if (environment.IsEnvironment("Testing")
            && target.Value.Database is not TenantDatabaseValidation.Primary and not TenantDatabaseValidation.Secondary)
            throw Unavailable();
        foreach (string control in new[] { PlatformStorage.ConnectionName, "Security" })
            if (target == Target(configuration.GetConnectionString(control))) throw Unavailable();
        var tenants = await directory.ListAsync(cancellationToken);
        if (!tenants.Any(row => row.Id == tenant.Id && row.Active && row.DatabaseReference == tenant.DatabaseReference)) throw Unavailable();
        foreach (var other in tenants.Where(row => row.Id != tenant.Id))
            if (target == Target(configuration.GetConnectionString(ConnectionPrefix + other.DatabaseReference))) throw Unavailable();
        return connection!;
    }

    private static (string Host, int Port, string Database)? Target(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection)) return null;
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(connection);
            if (string.IsNullOrWhiteSpace(parsed.Host) || string.IsNullOrWhiteSpace(parsed.Database)) return null;
            return (parsed.Host.ToUpperInvariant(), parsed.Port, parsed.Database);
        }
        catch (ArgumentException) { return null; } // Never retain the potentially sensitive parser exception.
    }

    private static TenantAccessException Unavailable() => new(TenantAccessException.DatabaseUnavailable, configurationFailure: true);
}
