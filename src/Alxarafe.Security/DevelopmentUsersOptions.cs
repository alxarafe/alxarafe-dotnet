namespace Alxarafe.Security;

// Modules contribute development/test fixtures through native options. The Host
// provisions identities without knowing any module's permissions or domain.
public sealed class DevelopmentUsersOptions
{
    public IList<DevelopmentUser> Users { get; } = new List<DevelopmentUser>();
}

public sealed record DevelopmentUser(string Email, string Password, IReadOnlyCollection<string> Permissions);
