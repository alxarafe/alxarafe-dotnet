namespace Alxarafe.Core;

// TenantId supplied to the read port is the canonical Tenant ID, never a user ID.
public sealed record TenantAiSettings(string TenantId, string? ProviderOverride = null, string? ModelOverride = null)
{
    public const int MaximumTenantIdLength = 256;
    public const int MaximumSelectionLength = 256;
}

public interface ITenantAiSettingsStore
{
    Task<TenantAiSettings?> FindAsync(string tenantId, CancellationToken cancellationToken = default);
}

public interface ICurrentTenantContext
{
    string? TenantId { get; }
}

// Explicit unavailable context for non-request compositions. The Host registers authoritative tenancy.
public sealed class UnavailableTenantContext : ICurrentTenantContext
{
    public string? TenantId => null;
}
