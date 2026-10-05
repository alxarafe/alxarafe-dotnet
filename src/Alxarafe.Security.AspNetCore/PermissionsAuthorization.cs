using Alxarafe.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Alxarafe.Security.AspNetCore;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(PermissionClaim.Type, requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed class PermissionPolicyProvider(
    IEnumerable<IPermissionDefinitionProvider> providers,
    DefaultAuthorizationPolicyProvider fallback) : IAuthorizationPolicyProvider
{
    private readonly HashSet<string> _permissions = providers
        .SelectMany(provider => provider.GetPermissions())
        .Select(permission => permission.Name)
        .ToHashSet(StringComparer.Ordinal);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!_permissions.Contains(policyName)) return Task.FromResult<AuthorizationPolicy?>(null);
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => fallback.GetFallbackPolicyAsync();
}

public static class PermissionAuthorizationExtensions
{
    public static IServiceCollection AddAlxarafePermissionAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider>(provider =>
            new PermissionPolicyProvider(
                provider.GetServices<IPermissionDefinitionProvider>(),
                new DefaultAuthorizationPolicyProvider(provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>() )));
        return services;
    }
}
