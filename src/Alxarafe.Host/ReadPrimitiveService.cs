using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Alxarafe.Core;
using Alxarafe.Modularity;
using Alxarafe.Resources.Runtime;
using Microsoft.AspNetCore.Authorization;

namespace Alxarafe.Host;

internal sealed partial class ReadPrimitiveService(ReadPrimitiveRegistry registry, ModuleStateService states,
    ICurrentSecurityContext security, IAuthorizationService authorization, IEnumerable<IReadPrimitiveExecutor> executors,
    ILogger<ReadPrimitiveService> logger) : IReadPrimitiveService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<IReadOnlyList<ReadPrimitiveDescriptor>> EffectiveAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var current = await security.ResolveAsync(principal, ct);
        if (current is null) return [];
        var result = new List<ReadPrimitiveDescriptor>();
        foreach (var primitive in registry.Primitives)
            if (await states.IsEnabledAsync(primitive.Module, ct) && await AllowedAsync(current, primitive)) result.Add(primitive);
        return result;
    }
    private async Task<bool> AllowedAsync(ClaimsPrincipal principal, ReadPrimitiveDescriptor primitive)
    {
        foreach (string permission in primitive.RequiredPermissions)
            if (!(await authorization.AuthorizeAsync(principal, permission)).Succeeded) return false;
        return true;
    }
    public async Task<ReadPrimitiveResult> InvokeAsync(ReadPrimitiveCommand command, CancellationToken ct = default)
    {
        string invocationId = Guid.NewGuid().ToString("N");
        long started = Stopwatch.GetTimestamp();
        var primitive = registry.Primitives.SingleOrDefault(primitive => primitive.Id == command.PrimitiveId || primitive.Name == command.PrimitiveId);
        var outcome = ReadPrimitiveOutcome.Failed;
        long? offset = null, total = null;
        int? limit = null, returned = null, outputBytes = null;
        try
        {
            if (primitive is null) return Finish(ReadPrimitiveOutcome.NotFound);
            // Never reuse a conversation/request permission snapshot. A fresh persisted
            // identity is required immediately before this individual execution.
            var current = await security.ResolveAsync(command.Principal, ct);
            if (current is null || !await AllowedAsync(current, primitive)) return Finish(ReadPrimitiveOutcome.Denied);
            if (!await states.IsEnabledAsync(primitive.Module, ct)) return Finish(ReadPrimitiveOutcome.Unavailable);
            if (command.Input.ValueKind != JsonValueKind.Object) return Finish(ReadPrimitiveOutcome.InvalidRequest);
            var executor = executors.SingleOrDefault(executor => executor.CanExecute(primitive.Id));
            if (executor is null) return Finish(ReadPrimitiveOutcome.Failed);
            var context = new ReadPrimitiveContext(invocationId, command.AiInvocationId, command.CorrelationId,
                current.FindFirstValue(ClaimTypes.NameIdentifier), command.Sequence);
            var output = await executor.ExecuteAsync(primitive, command.Input, context, ct);
            var normalized = JsonSerializer.SerializeToElement(output, Json);
            outputBytes = System.Text.Encoding.UTF8.GetByteCount(normalized.GetRawText());
            // Bounded paging metadata makes composition cost observable without payload logging.
            if (normalized.ValueKind == JsonValueKind.Object)
            {
                if (normalized.TryGetProperty(Alxarafe.AspNetCore.CollectionPagination.OffsetParameter, out var position) && position.TryGetInt64(out long pageOffset)) offset = pageOffset;
                if (normalized.TryGetProperty(Alxarafe.AspNetCore.CollectionPagination.LimitParameter, out var capacity) && capacity.TryGetInt32(out int pageLimit)) limit = pageLimit;
                if (normalized.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array) returned = items.GetArrayLength();
                if (normalized.TryGetProperty("total", out var count) && count.TryGetInt64(out long totalCount)) total = totalCount;
            }
            return Finish(ReadPrimitiveOutcome.Succeeded, normalized);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TenantAccessException failure) { return Finish(failure.ConfigurationFailure ? ReadPrimitiveOutcome.Unavailable : ReadPrimitiveOutcome.Denied); }
        catch (ResourceRuntimeException failure)
        {
            return Finish(failure.Code switch
            {
                ResourceRuntimeError.Forbidden or ResourceRuntimeError.TenantRequired => ReadPrimitiveOutcome.Denied,
                ResourceRuntimeError.ModuleDisabled => ReadPrimitiveOutcome.Unavailable,
                ResourceRuntimeError.ValidationFailed => ReadPrimitiveOutcome.InvalidRequest,
                ResourceRuntimeError.ResourceNotFound => ReadPrimitiveOutcome.NotFound,
                _ => ReadPrimitiveOutcome.Failed
            });
        }
        catch (ReadPrimitiveInputException) { return Finish(ReadPrimitiveOutcome.InvalidRequest); }
        catch (Exception) { return Finish(ReadPrimitiveOutcome.Failed); }
        finally
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                string? subject = command.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
                double duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                string status = outcome.ToString();
                LogAttempt(logger, invocationId, command.AiInvocationId, primitive?.Id, primitive?.Module,
                    subject, command.Sequence, duration, status, command.CorrelationId, offset, limit, returned, total, outputBytes);
            }
        }
        ReadPrimitiveResult Finish(ReadPrimitiveOutcome result, JsonElement? output = null)
        {
            outcome = result;
            return new(result, result == ReadPrimitiveOutcome.Succeeded ? invocationId : null, output,
                result == ReadPrimitiveOutcome.Succeeded ? primitive!.Id : null);
        }
    }
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "READ primitive invocation {InvocationId} aiInvocationId={AiInvocationId} primitive={PrimitiveId} module={Module} subject={Subject} sequence={Sequence} durationMs={DurationMs:F3} outcome={Outcome} correlationId={CorrelationId} offset={Offset} limit={Limit} returned={Returned} total={Total} outputBytes={OutputBytes}")]
    private static partial void LogAttempt(ILogger logger, string invocationId, string aiInvocationId, string? primitiveId,
        string? module, string? subject, int sequence, double durationMs, string outcome, string? correlationId, long? offset, int? limit, int? returned, long? total, int? outputBytes);
}
