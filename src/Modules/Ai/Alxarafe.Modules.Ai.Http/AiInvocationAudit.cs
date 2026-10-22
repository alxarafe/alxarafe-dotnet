using System.Diagnostics;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Alxarafe.Modules.Ai.Http;

public sealed class AiInvocationMetadata;

public sealed record AiInvocationAuditContext(IReadOnlyList<AiToolUsage> ToolsUsed, string? FailureCode);

public sealed partial class AiInvocationAuditMiddleware(RequestDelegate next, ILogger<AiInvocationAuditMiddleware> logger)
{
    public const string InvocationIdItemKey = "erbas.ai.invocation_id";
    public const string AuditContextItemKey = "erbas.ai.audit_context";
    private const string Completed = "completed";
    private const string Denied = "denied";
    private const string Failed = "failed";
    private const string NoTools = "-";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<AiInvocationMetadata>() is null)
        {
            await next(context);
            return;
        }
        string invocationId = Guid.NewGuid().ToString("N");
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        long started = Stopwatch.GetTimestamp();
        context.Items[InvocationIdItemKey] = invocationId;
        bool exception = false;
        try { await next(context); }
        catch
        {
            exception = true;
            throw;
        }
        finally
        {
            var status = exception ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
            var audit = context.Items[AuditContextItemKey] as AiInvocationAuditContext;
            var (outcome, errorCode) = status switch
            {
                StatusCodes.Status200OK => (Completed, (string?)null),
                StatusCodes.Status400BadRequest => (Denied, AiContract.InvalidRequest),
                StatusCodes.Status401Unauthorized => (Denied, AiContract.Unauthorized),
                StatusCodes.Status403Forbidden => (Denied, AiContract.Forbidden),
                StatusCodes.Status500InternalServerError => (Failed, audit?.FailureCode ?? AiContract.AssistantExecutionFailed),
                StatusCodes.Status503ServiceUnavailable => (Failed, audit?.FailureCode ?? AiContract.ModuleDisabled),
                _ => (Failed, (string?)null)
            };
            string? subject = context.Items[typeof(CoreUserState)] is CoreUserState state ? state.Id.ToString() : null;
            string tools = audit?.ToolsUsed is { Count: > 0 }
                ? string.Join(",", audit.ToolsUsed.Select(tool => tool.CapabilityId + ":" + tool.InvocationId))
                : NoTools;
            double durationMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            LogAttempt(logger, invocationId, subject, timestamp, outcome, durationMilliseconds, errorCode,
                context.TraceIdentifier, tools);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "AI invocation {InvocationId} subject={Subject} timestamp={Timestamp:o} outcome={Outcome} durationMs={DurationMilliseconds:F3} error={ErrorCode} correlationId={CorrelationId} tools={Tools}")]
    private static partial void LogAttempt(ILogger logger, string invocationId, string? subject, DateTimeOffset timestamp,
        string outcome, double durationMilliseconds, string? errorCode, string correlationId, string tools);
}
