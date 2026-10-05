namespace Alxarafe.Security;

public sealed record PermissionDefinition(string Name, string? Description = null);

public interface IPermissionDefinitionProvider
{
    IEnumerable<PermissionDefinition> GetPermissions();
}

public static class PermissionClaim
{
    public const string Type = "permission";
}
