using Alxarafe.Core;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.EntityFrameworkCore;

public sealed class TenantAiSettingsRow
{
    public string TenantId { get; set; } = string.Empty;
    public string? CanonicalTenantId { get; set; }
    public string? ProviderOverride { get; set; }
    public string? ModelOverride { get; set; }
}

public static class TenantAiSettingsStorage
{
    public const string Table = "tenant_ai_settings";
    public const string TenantIdColumn = "tenant_id";
    public const string ProviderColumn = "provider_override";
    public const string ModelColumn = "model_override";
    public const string PrimaryKey = "PK_tenant_ai_settings";
    public const string TenantIdConstraint = "CK_tenant_ai_settings_tenant_id";
    public const string TenantIdCheck = "btrim(tenant_id) <> ''";

    internal static void Configure(ModelBuilder modelBuilder)
    {
        var settings = modelBuilder.Entity<TenantAiSettingsRow>();
        settings.ToTable(Table, table => table.HasCheckConstraint(TenantIdConstraint, TenantIdCheck));
        settings.HasKey(row => row.TenantId).HasName(PrimaryKey);
        settings.Property(row => row.TenantId).HasColumnName(TenantIdColumn)
            .HasMaxLength(TenantAiSettings.MaximumTenantIdLength).UseCollation(PlatformStorage.IdCollation).ValueGeneratedNever();
        settings.Property(row => row.ProviderOverride).HasColumnName(ProviderColumn).HasMaxLength(TenantAiSettings.MaximumSelectionLength);
        settings.Property(row => row.ModelOverride).HasColumnName(ModelColumn).HasMaxLength(TenantAiSettings.MaximumSelectionLength);
    }
}

public sealed class EfTenantAiSettingsStore(IDbContextFactory<PlatformDbContext> factory) : ITenantAiSettingsStore
{
    public async Task<TenantAiSettings?> FindAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.TenantAiSettings.AsNoTracking().Where(row => row.CanonicalTenantId == tenantId)
            .Select(row => new TenantAiSettings(row.CanonicalTenantId!, row.ProviderOverride, row.ModelOverride))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
