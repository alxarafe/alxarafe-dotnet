using Alxarafe.Modularity;
using Alxarafe.Resources.Runtime;
using Microsoft.AspNetCore.Authorization;

namespace Alxarafe.Host;

internal sealed class CurrentResourceAuthorization(IHttpContextAccessor http, ICurrentSecurityContext security,
    IAuthorizationService authorization, IAuthorizationPolicyProvider policies) : IResourceAuthorization
{
    public async Task<bool> IsAllowedAsync(IReadOnlyList<string> requiredPermissions, CancellationToken cancellationToken = default)
    {
        var principal = http.HttpContext?.User;
        var current = principal is null ? null : await security.ResolveAsync(principal, cancellationToken);
        if (current is null) return false;
        foreach (string permission in requiredPermissions)
            if (await policies.GetPolicyAsync(permission) is null || !(await authorization.AuthorizeAsync(current, permission)).Succeeded) return false;
        return true;
    }
}
