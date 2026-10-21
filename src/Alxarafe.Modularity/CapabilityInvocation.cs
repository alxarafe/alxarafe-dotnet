using System.Numerics;

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
