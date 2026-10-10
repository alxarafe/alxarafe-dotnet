using System.Security.Claims;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Alxarafe.Host;

internal sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserAdministration users)
    {
        if (SharedUsersEndpoint.IsPath(context.Request.Path)) context.Response.Headers.CacheControl = "no-store";
        var metadata = context.GetEndpoint()?.Metadata;
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

internal sealed class SharedUserAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (SharedUsersEndpoint.IsPath(context.Request.Path) && (result.Challenged || result.Forbidden))
        {
            if (result.Challenged) context.Response.Headers.WWWAuthenticate = "Bearer";
            await UserEndpoints.Error(result.Challenged ? 401 : 403, result.Challenged ? "unauthorized" : "forbidden").ExecuteAsync(context);
            return;
        }
        await fallback.HandleAsync(next, context, policy, result);
    }
}

internal static class SharedUsersEndpoint
{
    public static bool IsPath(PathString path) => path == "/api/auth/me" || path == "/api/users" || path.StartsWithSegments("/api/users");
    public static AuthorizationPolicy AdminPolicy { get; } = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().RequireClaim(CoreUserClaims.Admin, "true").Build();
}
