using System.Security.Claims;
using Alxarafe.Security;
using Alxarafe.Security.AspNetCore;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace Alxarafe.Host;

internal sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserAdministration users)
    {
        var metadata = context.GetEndpoint()?.Metadata;
        if (SharedCoreEndpoint.IsPath(context.Request.Path) || metadata?.GetMetadata<SharedContractErrorsMetadata>() is not null)
            context.Response.Headers.CacheControl = "no-store";
        bool protectedResource = metadata?.GetMetadata<IAllowAnonymous>() is null &&
            (metadata?.GetMetadata<IAuthorizeData>() is not null || metadata?.GetMetadata<AuthorizationPolicy>() is not null);
        if (protectedResource && context.User.Identity?.IsAuthenticated == true)
        {
            var state = Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                ? await users.FindAsync(id) : null;
            if (state is null || !state.Enabled) context.User = new ClaimsPrincipal(new ClaimsIdentity());
            else
            {
                context.Items[typeof(CoreUserState)] = state;
                foreach (var identity in context.User.Identities)
                    foreach (var claim in identity.Claims.Where(claim => claim.Type == CoreUserClaims.Admin || claim.Type == CoreUserClaims.Enabled).ToArray())
                        identity.RemoveClaim(claim);
                if (state.Admin) ((ClaimsIdentity)context.User.Identity!).AddClaim(new Claim(CoreUserClaims.Admin, "true"));
            }
        }
        await next(context);
    }
}

internal static class SharedCoreEndpoint
{
    public static bool IsPath(PathString path) => path == "/api/auth/me" || path == "/api/users" || path.StartsWithSegments("/api/users")
        || path == ModuleEndpoints.Path || path.StartsWithSegments(ModuleEndpoints.Path);
}
