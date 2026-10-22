using System.Numerics;
using System.Security.Claims;
using System.Text.Json;

namespace Alxarafe.Modularity;

public sealed record CapabilityListInput(BigInteger Offset, int Limit);

public sealed record CapabilityExecutionContext(string InvocationId, string? CorrelationId, string? Subject,
    string CapabilityId, string Module, IReadOnlyList<string> EffectivePermissions, string Caller = CapabilityExecutionContext.UserCaller)
{
    public const string UserCaller = "user";
}

public interface ICapabilityExecutor
{
    bool CanExecute(string capabilityId);
    Task<object?> ExecuteAsync(CapabilityDescriptor capability, CapabilityListInput input,
        CapabilityExecutionContext context, CancellationToken cancellationToken = default);
}

public enum CapabilityInvocationOutcome { Succeeded, InvalidRequest, NotFound, ExecutionFailed }

public sealed record CapabilityInvocationCommand(string CapabilityId, Func<CancellationToken, Task<JsonElement?>> ReadInput,
    ClaimsPrincipal Principal, string? Subject, string? CorrelationId, string? InvocationId = null, bool Audit = true);

public sealed record CapabilityInvocationResult(CapabilityInvocationOutcome Outcome, string? InvocationId = null,
    JsonElement? Output = null);

public interface ICapabilityInvocationService
{
    Task<IReadOnlyList<CapabilityDescriptor>> EffectiveAsync(ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);

    Task<CapabilityInvocationResult> InvokeAsync(CapabilityInvocationCommand command,
        CancellationToken cancellationToken = default);
}
