using System.Collections.ObjectModel;
using Alxarafe.Resources;

namespace Alxarafe.Resources.Runtime;

public sealed class ResourceRecord
{
    public IReadOnlyDictionary<string, ScalarValue> Values { get; }
    public ResourceRecord(IEnumerable<KeyValuePair<string, ScalarValue>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = new SortedDictionary<string, ScalarValue>(StringComparer.Ordinal);
        foreach (var pair in values)
            if (!copy.TryAdd(pair.Key, pair.Value)) throw new ArgumentException("Duplicate resource field.", nameof(values));
        Values = new ReadOnlyDictionary<string, ScalarValue>(copy);
    }
}
public sealed record ResourceIdentifier(string Field, ScalarValue Value);
public enum ResourceRuntimeError
{
    ResourceNotFound, OperationNotSupported, RepositoryNotRegistered, ValidationFailed,
    Forbidden, TenantRequired, ModuleDisabled, ExecutionFailed, InvalidRepositoryResult,
    ActionNotFound, ActionHandlerNotRegistered, ActionUnavailable, ActionConflict, Conflict
}
public enum ResourceInputError { UnknownField, Required, InvalidValue, ReadOnly, Immutable, IdentifierMutation, InvalidQuery, BusinessRule }
public sealed record ResourceIssue(string Path, ResourceInputError Code, string Message);
public sealed class ResourceRuntimeException : InvalidOperationException
{
    public string ResourceId { get; }
    public ResourceRuntimeError Code { get; }
    public IReadOnlyList<ResourceIssue> Issues { get; }
    public string? ReasonCode { get; }
    public ResourceRuntimeException(string resourceId, ResourceRuntimeError code, IEnumerable<ResourceIssue>? issues = null, string? reasonCode = null)
        : base($"Resource '{resourceId}': {code}.")
    {
        ResourceId = resourceId; Code = code; ReasonCode = reasonCode; Issues = Array.AsReadOnly(issues?.ToArray() ?? []);
    }
}

// The adapter owns its scoped persistence mapping and mutation atomicity.
public interface IResourceRepository
{
    string ResourceId { get; }
    Task<ResourcePage> ListAsync(ResourceQuery query, CancellationToken cancellationToken = default);
    Task<ResourceRecord?> GetAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default);
    Task<ResourceRecord> CreateAsync(ResourceRecord values, CancellationToken cancellationToken = default);
    Task<ResourceRecord?> UpdateAsync(ResourceIdentifier identifier, ResourceRecord patch, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default);
}

// Receives metadata-declared permissions only from orchestration; the adapter obtains
// the authoritative current principal. No caller principal/permission list in CRUD APIs.
public interface IResourceAuthorization
{
    Task<bool> IsAllowedAsync(IReadOnlyList<string> requiredPermissions, CancellationToken cancellationToken = default);
}
public sealed record ResourceOperationContext(ResourceDefinition Definition, ResourceOperation Operation,
    ResourceIdentifier? Identifier, ResourceRecord? Current, ResourceRecord? Values);
public interface IResourceOperationValidator
{
    string ResourceId { get; }
    string Id { get; }
    Task<IReadOnlyList<ResourceIssue>> ValidateAsync(ResourceOperationContext context, CancellationToken cancellationToken = default);
}
