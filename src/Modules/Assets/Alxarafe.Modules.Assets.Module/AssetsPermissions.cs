using Alxarafe.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Alxarafe.Modules.Assets;

public static class AssetsPermissions
{
    public const string Read = "assets.read";
    public const string Maintain = "assets.maintain";
    public const string SchemaManage = "assets.schema.manage";
    public const string Sensitive = "assets.actions.sensitive";
    public const string Assignments = "assets.assignments.manage";
    public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[] { Read, Maintain, SchemaManage, Sensitive, Assignments });

    internal static void Configure(IServiceCollection services) => services.AddAuthorization(options =>
    {
        foreach (string permission in All)
        {
            string[] required = permission switch
            {
                Read => [Read],
                Sensitive => [Read, Maintain, Sensitive],
                _ => [Read, permission]
            };
            options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
                context.User.HasClaim(CoreUserClaims.Admin, "true")
                || required.All(value => context.User.HasClaim(PermissionClaim.Type, value))));
        }
    });
}
