using System.Security.Claims;
using Alxarafe.Modularity;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Host;

internal sealed class CurrentSecurityContext(SecurityDbContext database) : ICurrentSecurityContext
{
    public async Task<ClaimsPrincipal?> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        if (principal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        if (!await database.Users.AsNoTracking().AnyAsync(user => user.Id == id, ct)) return null;
        var persisted = await database.UserClaims.AsNoTracking().Where(claim => claim.UserId == id)
            .Select(claim => new { claim.ClaimType, claim.ClaimValue }).ToArrayAsync(ct);
        if (persisted.Any(claim => claim.ClaimType == CoreUserClaims.Enabled && claim.ClaimValue == "false")) return null;
        var claims = persisted.Where(claim => claim.ClaimType is not null && claim.ClaimValue is not null)
            .Select(claim => new Claim(claim.ClaimType!, claim.ClaimValue!)).ToList();
        claims.RemoveAll(claim => claim.Type == ClaimTypes.NameIdentifier);
        claims.Add(new Claim(ClaimTypes.NameIdentifier, id.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, principal.Identity.AuthenticationType));
    }
}
