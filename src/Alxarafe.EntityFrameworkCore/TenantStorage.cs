using Alxarafe.Core;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.EntityFrameworkCore;

public sealed class TenantRow
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Active { get; set; }
    public string DatabaseReference { get; set; } = string.Empty;
}

public sealed class TenantMembershipRow
{
    public Guid UserId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public bool Active { get; set; }
}

public static class TenantStorage
{
    public const string MetadataConstraint = "CK_tenants_metadata";
    public const string MetadataCheck = "btrim(id) <> '' AND btrim(key) <> '' AND btrim(display_name) <> '' AND database_reference ~ '^[A-Za-z0-9_-]+$'";
    public const string TenantsTable = "tenants";
    public const string MembershipsTable = "tenant_memberships";
    public const string CanonicalTenantColumn = "canonical_tenant_id";
    public const string CanonicalTenantIndex = "IX_tenant_ai_settings_canonical_tenant_id";
    public const string SettingsForeignKey = "FK_tenant_ai_settings_tenants";
    public const string MembershipForeignKey = "FK_tenant_memberships_tenants";

    internal static void Configure(ModelBuilder modelBuilder)
    {
        var tenants = modelBuilder.Entity<TenantRow>();
        tenants.ToTable(TenantsTable, table => table.HasCheckConstraint(MetadataConstraint, MetadataCheck));
        tenants.HasKey(row => row.Id);
        tenants.Property(row => row.Id).HasColumnName("id").HasMaxLength(Tenant.MaximumIdLength).UseCollation(PlatformStorage.IdCollation).ValueGeneratedNever();
        tenants.Property(row => row.Key).HasColumnName("key").HasMaxLength(Tenant.MaximumKeyLength).UseCollation(PlatformStorage.IdCollation).IsRequired();
        tenants.Property(row => row.DisplayName).HasColumnName("display_name").HasMaxLength(Tenant.MaximumNameLength).IsRequired();
        tenants.Property(row => row.Active).HasColumnName("active");
        tenants.Property(row => row.DatabaseReference).HasColumnName("database_reference").HasMaxLength(Tenant.MaximumKeyLength).UseCollation(PlatformStorage.IdCollation).IsRequired();
        tenants.HasIndex(row => row.Key).IsUnique();
        tenants.HasIndex(row => row.DatabaseReference).IsUnique();
        var memberships = modelBuilder.Entity<TenantMembershipRow>();
        memberships.ToTable(MembershipsTable);
        memberships.HasKey(row => new { row.UserId, row.TenantId });
        memberships.Property(row => row.UserId).HasColumnName("user_id");
        memberships.Property(row => row.TenantId).HasColumnName("tenant_id").HasMaxLength(Tenant.MaximumIdLength).UseCollation(PlatformStorage.IdCollation);
        memberships.Property(row => row.Active).HasColumnName("active");
        memberships.HasOne<TenantRow>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Cascade).HasConstraintName(MembershipForeignKey);
        var settings = modelBuilder.Entity<TenantAiSettingsRow>();
        settings.Property(row => row.CanonicalTenantId).HasColumnName(CanonicalTenantColumn).HasMaxLength(Tenant.MaximumIdLength).UseCollation(PlatformStorage.IdCollation);
        settings.HasIndex(row => row.CanonicalTenantId).IsUnique().HasDatabaseName(CanonicalTenantIndex);
        settings.HasOne<TenantRow>().WithMany().HasForeignKey(row => row.CanonicalTenantId).OnDelete(DeleteBehavior.Restrict).HasConstraintName(SettingsForeignKey);
    }
}

public sealed class EfTenantDirectory(IDbContextFactory<PlatformDbContext> contexts) : ITenantDirectory
{
    public async Task<IReadOnlyList<Tenant>> MembershipsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await (from membership in context.TenantMemberships.AsNoTracking()
                      join tenant in context.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
                      where membership.UserId == userId && membership.Active
                      select new Tenant(tenant.Id, tenant.Key, tenant.DisplayName, tenant.Active, tenant.DatabaseReference)).ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await context.Tenants.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new Tenant(row.Id, row.Key, row.DisplayName, row.Active, row.DatabaseReference)).ToArrayAsync(cancellationToken);
    }
}
