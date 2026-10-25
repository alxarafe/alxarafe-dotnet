using System.Text.RegularExpressions;

namespace Alxarafe.Resources.Runtime;

public sealed class ResourceActionInput
{
    public IReadOnlyDictionary<string, ScalarValue> Values { get; }
    public ResourceActionInput(IEnumerable<KeyValuePair<string, ScalarValue>>? values = null) => Values = new ResourceRecord(values ?? []).Values;
}
public abstract record ResourceActionTarget;
public sealed record ResourceEntityTarget(ResourceIdentifier Identifier) : ResourceActionTarget;
public sealed record ResourceCollectionTarget : ResourceActionTarget;
public sealed record ResourceSelectionTarget : ResourceActionTarget
{
    public IReadOnlyList<ResourceIdentifier> Identifiers { get; }
    public ResourceSelectionTarget(IEnumerable<ResourceIdentifier> identifiers) => Identifiers = Array.AsReadOnly(identifiers.ToArray());
}
public sealed record ResourceActionContext(ResourceDefinition Definition, ActionDefinition Action, ResourceActionTarget Target);
public sealed record ResourceActionExecutionContext(ResourceActionContext Context, ResourceActionInput Input);
public sealed record ResourceActionAvailability(bool Available, string? ReasonCode = null);
public sealed record ResourceActionState(string Name, bool Authorized, bool Available, string? UnavailableReason = null);
public enum ResourceActionOutcomeKind { Success, NotFound, Conflict }
public sealed record ResourceActionOutcome(ResourceActionOutcomeKind Kind, string? ReasonCode = null);

public interface IResourceActionHandler
{
    string ResourceId { get; }
    string ActionName { get; }
    Task<ResourceActionOutcome> ExecuteAsync(ResourceActionExecutionContext context, CancellationToken cancellationToken = default);
}
public interface IResourceActionAvailabilityProvider
{
    string ResourceId { get; }
    string ActionName { get; }
    Task<ResourceActionAvailability> EvaluateAsync(ResourceActionContext context, CancellationToken cancellationToken = default);
}
public static class ResourceActionReason
{
    public const int MaximumLength = 100;
    private static readonly Regex Codes = new(@"\A[a-z][a-z0-9]*(?:[_.-][a-z0-9]+)*\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    internal static bool Valid(string? code) => code is not null && code.Length <= MaximumLength && Codes.IsMatch(code);
}
