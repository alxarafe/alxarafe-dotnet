using Alxarafe.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Alxarafe.Security.AspNetCore;

public static class CoreAdministration
{
    public static AuthorizationPolicy Policy { get; } = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().RequireClaim(CoreUserClaims.Admin, "true").Build();
}

public sealed class SharedContractErrorsMetadata;

public static class SharedContractAuthorizationExtensions
{
    public static TBuilder WithSharedContractErrors<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new SharedContractErrorsMetadata());
}

public sealed class SharedContractAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<SharedContractErrorsMetadata>() is not null && (authorizeResult.Challenged || authorizeResult.Forbidden))
        {
            context.Response.Headers.CacheControl = "no-store";
            if (authorizeResult.Challenged) context.Response.Headers.WWWAuthenticate = "Bearer";
            await Results.Json(new { code = authorizeResult.Challenged ? "unauthorized" : "forbidden" },
                statusCode: authorizeResult.Challenged ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden).ExecuteAsync(context);
            return;
        }
        await fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}
