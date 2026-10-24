using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Claims;
using System.Text.Json;
using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Security;
using Microsoft.AspNetCore.Authorization;

namespace Alxarafe.Host;

internal sealed class CapabilityInvocationService(CapabilityRegistry registry, ModuleStateService states,
    IAuthorizationService authorization, ICurrentSecurityContext security, IEnumerable<ICapabilityExecutor> executors, ILoggerFactory loggerFactory)
    : ICapabilityInvocationService
{
    private const string ReadEffect = "read";
    private readonly ILogger _auditLogger = loggerFactory.CreateLogger<CapabilityInvocationAuditMiddleware>();

    public async Task<IReadOnlyList<CapabilityDescriptor>> EffectiveAsync(ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var current = await security.ResolveAsync(principal, cancellationToken);
        return current is null ? [] : await EffectiveCurrentAsync(current, cancellationToken);
    }

    private async Task<IReadOnlyList<CapabilityDescriptor>> EffectiveCurrentAsync(ClaimsPrincipal current, CancellationToken cancellationToken)
    {
        var result = new List<CapabilityDescriptor>();
        foreach (var capability in registry.Capabilities)
        {
            if (!await states.IsEnabledAsync(capability.Module, cancellationToken)) continue;
            bool allowed = true;
            foreach (var permission in capability.RequiredPermissions)
                if (!(await authorization.AuthorizeAsync(current, permission)).Succeeded) { allowed = false; break; }
            if (allowed) result.Add(capability);
        }
        return result;
    }

    public async Task<CapabilityInvocationResult> InvokeAsync(CapabilityInvocationCommand command,
        CancellationToken cancellationToken = default)
    {
        string invocationId = command.InvocationId ?? Guid.NewGuid().ToString("N");
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        long started = Stopwatch.GetTimestamp();
        if (!CapabilityRegistry.IsValidId(command.CapabilityId))
            return Finish(command, invocationId, timestamp, started, CapabilityInvocationOutcome.InvalidRequest);
        var current = await security.ResolveAsync(command.Principal, cancellationToken);
        if (current is null) return Finish(command, invocationId, timestamp, started, CapabilityInvocationOutcome.NotFound);
        var effective = await EffectiveCurrentAsync(current, cancellationToken);
        var capability = effective.FirstOrDefault(item => string.Equals(item.Id, command.CapabilityId, StringComparison.Ordinal));
        if (capability is null || capability.Effect != ReadEffect)
            return Finish(command, invocationId, timestamp, started, CapabilityInvocationOutcome.NotFound);
        var executor = executors.FirstOrDefault(candidate => candidate.CanExecute(capability.Id));
        if (executor is null)
            return Finish(command, invocationId, timestamp, started, CapabilityInvocationOutcome.NotFound);

        JsonElement? raw;
        try { raw = await command.ReadInput(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Finish(command, invocationId, timestamp, started, CapabilityInvocationOutcome.InvalidRequest, capability); }
        var input = raw is null ? null : ParseInput(raw.Value);
        if (input is null)
            return Finish(command, invocationId, timestamp, started, CapabilityInvocationOutcome.InvalidRequest, capability);

        var execution = new CapabilityExecutionContext(invocationId, command.CorrelationId, command.Subject,
            capability.Id, capability.Module, EffectivePermissions(current));
        try
        {
            var output = await executor.ExecuteAsync(capability, input, execution, cancellationToken);
            var normalized = CapabilityInvocationOutput.Normalize(capability.OutputSchema.Ref, output);
            if (normalized is null)
                return Finish(command, invocationId, timestamp, started, CapabilityInvocationOutcome.ExecutionFailed, capability);
            return Finish(command, invocationId, timestamp, started, CapabilityInvocationOutcome.Succeeded, capability,
                invocationId, normalized.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return Finish(command, invocationId, timestamp, started, CapabilityInvocationOutcome.ExecutionFailed, capability);
        }
    }

    private CapabilityInvocationResult Finish(CapabilityInvocationCommand command, string invocationId,
        DateTimeOffset timestamp, long started, CapabilityInvocationOutcome outcome,
        CapabilityDescriptor? capability = null, string? publicInvocationId = null, JsonElement? output = null)
    {
        if (command.Audit)
        {
            int status = outcome switch
            {
                CapabilityInvocationOutcome.Succeeded => StatusCodes.Status200OK,
                CapabilityInvocationOutcome.InvalidRequest => StatusCodes.Status400BadRequest,
                CapabilityInvocationOutcome.NotFound => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status500InternalServerError
            };
            var (auditOutcome, errorCode) = CapabilityInvocationAudit.Classify(status);
            double durationMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (_auditLogger.IsEnabled(LogLevel.Information))
                CapabilityInvocationAudit.LogAttempt(_auditLogger, invocationId, capability?.Id, capability?.Module, command.Subject,
                    timestamp, auditOutcome, durationMilliseconds, errorCode, command.CorrelationId ?? "");
        }
        return outcome == CapabilityInvocationOutcome.Succeeded
            ? new CapabilityInvocationResult(outcome, publicInvocationId, output)
            : new CapabilityInvocationResult(outcome);
    }

    private static CapabilityListInput? ParseInput(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        string? offsetText = null;
        string? limitText = null;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name == CollectionPagination.OffsetParameter && offsetText is null)
            {
                if (!IntegerText(property.Value, out offsetText)) return null;
            }
            else if (property.Name == CollectionPagination.LimitParameter && limitText is null)
            {
                if (!IntegerText(property.Value, out limitText)) return null;
            }
            else return null;
        }
        var pagination = CollectionPagination.Parse(offsetText, limitText);
        return pagination is null ? null : new CapabilityListInput(pagination.Offset, pagination.Limit);
    }

    private static bool IntegerText(JsonElement value, out string text)
    {
        text = "";
        if (value.ValueKind != JsonValueKind.Number) return false;
        text = value.GetRawText();
        return text.Length > 0 && text.All(character => character is >= '0' and <= '9');
    }

    private static string[] EffectivePermissions(ClaimsPrincipal principal) =>
        principal.FindAll(PermissionClaim.Type).Select(claim => claim.Value).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
}
