namespace Alxarafe.Resources.Runtime;

// Explicit domain/integrity failure shared by repositories, validators and handlers.
// Adapters own rollback; neither SQL exceptions nor human messages classify failures.
public sealed class ResourceConflictException : InvalidOperationException
{
    public const string ImmutableField = "immutable_field";
    public string ReasonCode { get; }
    public IReadOnlyList<ResourceIssue> Issues { get; }

    public ResourceConflictException(string reasonCode, IEnumerable<ResourceIssue>? issues = null)
        : base("Resource state conflicts with the requested operation.")
    {
        if (!ResourceActionReason.Valid(reasonCode)) throw new ArgumentException("Invalid conflict reason code.", nameof(reasonCode));
        ReasonCode = reasonCode;
        Issues = Array.AsReadOnly(issues?.ToArray() ?? []);
    }
}
