namespace Alxarafe.Security;

// Identity state, deliberately distinct from module permission claims.
public static class CoreUserClaims
{
    public const string Enabled = "erbas.core.enabled";
    public const string Admin = "erbas.core.admin";
}

public sealed record CoreUserState
{
    public Guid Id { get; init; }
    public required string Email { get; init; }
    public bool Enabled { get; init; }
    public bool Admin { get; init; }
}
public enum UserUpdateOutcome { Updated, NotFound, LastAdmin }
public sealed record UserUpdateResult(UserUpdateOutcome Outcome, CoreUserState? User = null);
