using System.Diagnostics;
using Alxarafe.Security;
using Microsoft.AspNetCore.Http;

namespace Alxarafe.Host;

public sealed class CapabilityInvocationMetadata;

public sealed record CapabilityInvocationAuditContext(string CapabilityId, string Module, string? Subject);

internal static partial class CapabilityInvocationAudit
{
    public const string Completed = "completed";
    public const string Denied = "denied";
    public const string Failed = "failed";
    public const string Unauthorized = "unauthorized";

    public static (string Outcome, string? ErrorCode) Classify(int status) => status switch
    {
        StatusCodes.Status200OK => (Completed, null),
        StatusCodes.Status400BadRequest => (Denied, ModuleEndpoints.InvalidRequest),
        StatusCodes.Status401Unauthorized => (Denied, Unauthorized),
        StatusCodes.Status404NotFound => (Denied, CapabilityEndpoints.NotFound),
        StatusCodes.Status500InternalServerError => (Failed, CapabilityEndpoints.ExecutionFailed),
        _ => (Failed, null)
    };

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Capability invocation {InvocationId} capability={CapabilityId} module={Module} subject={Subject} timestamp={Timestamp:o} outcome={Outcome} durationMs={DurationMilliseconds:F3} error={ErrorCode} correlationId={CorrelationId}")]
    public static partial void LogAttempt(ILogger logger, string invocationId, string? capabilityId, string? module,
        string? subject, DateTimeOffset timestamp, string outcome, double durationMilliseconds, string? errorCode, string correlationId);
}

public sealed partial class CapabilityInvocationAuditMiddleware(RequestDelegate next, ILogger<CapabilityInvocationAuditMiddleware> logger)
{
    public const string InvocationIdItemKey = "alxarafe.core.capability.invocation_id";
    public const string AuditContextItemKey = "alxarafe.core.capability.audit_context";
    public const string SuppressItemKey = "alxarafe.core.capability.audit_suppressed";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<CapabilityInvocationMetadata>() is null)
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
            if (context.Items[SuppressItemKey] is not true)
            {
                var status = exception ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
                var (outcome, errorCode) = CapabilityInvocationAudit.Classify(status);
                var audit = context.Items[AuditContextItemKey] as CapabilityInvocationAuditContext;
                string? subject = audit?.Subject
                    ?? (context.Items[typeof(CoreUserState)] is CoreUserState state ? state.Id.ToString() : null);
                double durationMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (logger.IsEnabled(LogLevel.Information))
                    CapabilityInvocationAudit.LogAttempt(logger, invocationId, audit?.CapabilityId, audit?.Module, subject,
                        timestamp, outcome, durationMilliseconds, errorCode, context.TraceIdentifier);
            }
        }
    }
}
