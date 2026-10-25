namespace Alxarafe.Core;

public sealed record Tenant(string Id, string Key, string DisplayName, bool Active, string DatabaseReference)
{
    public const int MaximumIdLength = 256;
    public const int MaximumKeyLength = 128;
    public const int MaximumNameLength = 256;
}

public interface ITenantDirectory
{
    Task<IReadOnlyList<Tenant>> MembershipsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IRequestTenantContext : ICurrentTenantContext
{
    Task<Tenant> RequireAsync(CancellationToken cancellationToken = default);
}

public interface ITenantDatabaseResolver
{
    Task<string> ResolveAsync(CancellationToken cancellationToken = default);
}

public interface ITenantDatabaseMigrationRunner
{
    Task MigrateAsync(Func<string, CancellationToken, Task> migrate, CancellationToken cancellationToken = default);
}

// Registered only by explicit Development/Testing bootstrap, never Production.
public interface IDevelopmentTenantMembership
{
    Task AssignAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class TenantAccessException(string code, bool configurationFailure = false) : Exception(code)
{
    public const string Required = "tenant_required";
    public const string Inactive = "tenant_inactive";
    public const string SelectionRequired = "tenant_selection_required";
    public const string ContextChanged = "tenant_context_changed";
    public const string DatabaseUnavailable = "tenant_database_unavailable";
    public string Code { get; } = code;
    public bool ConfigurationFailure { get; } = configurationFailure;
}

// Supported validation workflow supplies only these disposable business databases.
public static class TenantDatabaseValidation
{
    public const string Primary = "alxarafe_business_test";
    public const string Secondary = "alxarafe_business_test_b";
}
