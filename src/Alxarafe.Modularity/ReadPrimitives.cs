using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Alxarafe.Modularity;

public sealed record ReadPrimitiveDescriptor(string Id, string Module, string Name, string Description,
    JsonElement InputSchema, IReadOnlyList<string> RequiredPermissions);
public interface IReadPrimitiveProvider
{
    IEnumerable<ReadPrimitiveDescriptor> GetReadPrimitives();
}
public sealed record ReadPrimitiveContext(string InvocationId, string AiInvocationId, string? CorrelationId,
    string? Subject, int Sequence);
public interface IReadPrimitiveExecutor
{
    bool CanExecute(string primitiveId);
    Task<object?> ExecuteAsync(ReadPrimitiveDescriptor primitive, JsonElement input, ReadPrimitiveContext context, CancellationToken ct);
}
public sealed class ReadPrimitiveInputException : Exception;
public enum ReadPrimitiveOutcome { Succeeded, Denied, Unavailable, NotFound, InvalidRequest, Failed }
public sealed record ReadPrimitiveResult(ReadPrimitiveOutcome Outcome, string? InvocationId = null, JsonElement? Output = null, string? PrimitiveId = null);
public sealed record ReadPrimitiveCommand(string PrimitiveId, JsonElement Input, ClaimsPrincipal Principal,
    string AiInvocationId, string? CorrelationId, int Sequence);
public interface IReadPrimitiveService
{
    Task<IReadOnlyList<ReadPrimitiveDescriptor>> EffectiveAsync(ClaimsPrincipal principal, CancellationToken ct = default);
    Task<ReadPrimitiveResult> InvokeAsync(ReadPrimitiveCommand command, CancellationToken ct = default);
}

// The security adapter resolves this from its authoritative identity store on every call.
public interface ICurrentSecurityContext
{
    Task<ClaimsPrincipal?> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}

public sealed class ReadPrimitiveRegistry
{
    private static readonly Regex ToolNameSyntax = new("\\A[a-z][a-z0-9_]{0,63}\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public IReadOnlyList<ReadPrimitiveDescriptor> Primitives { get; }
    public ReadPrimitiveRegistry(ModuleRegistry modules, CapabilityRegistry capabilities)
    {
        var names = capabilities.Capabilities.Select(capability => capability.Id.Replace('.', '_')).ToHashSet(StringComparer.Ordinal);
        var all = new SortedDictionary<string, ReadPrimitiveDescriptor>(StringComparer.Ordinal);
        foreach (var module in modules.Modules.OfType<IReadPrimitiveProvider>())
        {
            string owner = modules.GetDescriptor((IAlxarafeModule)module).Id;
            foreach (var primitive in module.GetReadPrimitives())
            {
                if (primitive is null || !CapabilityRegistry.IsValidId(primitive.Id) || primitive.Module != owner
                    || !ToolNameSyntax.IsMatch(primitive.Name) || !names.Add(primitive.Name)
                    || string.IsNullOrWhiteSpace(primitive.Description) || primitive.InputSchema.ValueKind != JsonValueKind.Object
                    || !primitive.InputSchema.TryGetProperty("type", out var type) || type.GetString() != "object"
                    || !primitive.InputSchema.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False
                    || !primitive.InputSchema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object
                    || primitive.RequiredPermissions.Count == 0 || primitive.RequiredPermissions.Any(string.IsNullOrWhiteSpace)
                    || !all.TryAdd(primitive.Id, primitive)) throw new InvalidOperationException("Invalid or duplicate READ primitive registration.");
            }
        }
        Primitives = Array.AsReadOnly(all.Values.ToArray());
    }
}
