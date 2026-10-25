using System.Security.Claims;
using Alxarafe.Core;
using Alxarafe.Modularity;

namespace Alxarafe.Host;

public sealed class TenantContext(IHttpContextAccessor http, ICurrentSecurityContext security, ITenantDirectory directory) : IRequestTenantContext
{
    private Tenant? selected;
    public string? TenantId => selected?.Id;

    public async Task<Tenant> RequireAsync(CancellationToken cancellationToken = default)
    {
        var principal = http.HttpContext?.User;
        var current = principal is null ? null : await security.ResolveAsync(principal, cancellationToken);
        if (current is null || !Guid.TryParse(current.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new TenantAccessException(TenantAccessException.Required);
        var memberships = await directory.MembershipsAsync(userId, cancellationToken);
        var active = memberships.Where(tenant => tenant.Active).ToArray();
        if (active.Length == 0)
            throw new TenantAccessException(memberships.Count > 0 ? TenantAccessException.Inactive : TenantAccessException.Required);
        if (active.Length != 1) throw new TenantAccessException(TenantAccessException.SelectionRequired);
        if (selected is not null && (selected.Id != active[0].Id || selected.DatabaseReference != active[0].DatabaseReference)) throw new TenantAccessException(TenantAccessException.ContextChanged);
        selected = active[0];
        return selected;
    }
}
