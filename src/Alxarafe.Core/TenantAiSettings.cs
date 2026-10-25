namespace Alxarafe.Core;

// Opaque identifier reserved for a future authoritative tenant source, not a user ID.
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

// Runtime has no tenant membership/identity authority yet. Never infer one from HTTP or user data.
public sealed class UnavailableTenantContext : ICurrentTenantContext
{
    public string? TenantId => null;
}
