using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Host;

public sealed class DevelopmentTenantBootstrap(IDbContextFactory<PlatformDbContext> contexts) : IDevelopmentTenantMembership
{
    public const string EnabledConfiguration = "Tenancy:DevelopmentBootstrap";
    public const string TenantId = "00000000-0000-0000-0000-000000000001";
    public const string TenantKey = "development";
    public const string DatabaseReference = "development";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO " + TenantStorage.TenantsTable + " (id, key, display_name, active, database_reference) VALUES ({0}, {1}, {2}, TRUE, {3}) ON CONFLICT (id) DO NOTHING",
            [TenantId, TenantKey, "Synthetic development tenant", DatabaseReference], cancellationToken);
        var existing = await context.Tenants.SingleAsync(row => row.Id == TenantId, cancellationToken);
        if (!existing.Active || existing.Key != TenantKey || existing.DatabaseReference != DatabaseReference)
            throw new InvalidOperationException("Development tenant bootstrap conflicts with existing registry state.");
    }

    public async Task AssignAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        // Atomic, idempotent bootstrap only. Never replace existing memberships or resolve by default.
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO " + TenantStorage.MembershipsTable + " (user_id, tenant_id, active) SELECT {0}, {1}, TRUE WHERE NOT EXISTS (SELECT 1 FROM "
            + TenantStorage.MembershipsTable + " WHERE user_id = {0}) ON CONFLICT DO NOTHING",
            [userId, TenantId], cancellationToken);
    }
}
