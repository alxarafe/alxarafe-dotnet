using Alxarafe.Modules.Ai.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alxarafe.Modules.Ai.Persistence;

// Immutable event IDs and a unique logical-call/attempt key make repeat writes
// idempotent. Each actual provider retry remains its own event.
public sealed class EfAiUsageMeter(IDbContextFactory<AiUsageDbContext> contexts, ILogger<EfAiUsageMeter> logger) : IAiUsageMeter
{
    private static readonly Action<ILogger, string, Exception?> RecordingFailed = LoggerMessage.Define<string>(
        LogLevel.Warning, new EventId(1, nameof(RecordingFailed)), "AI usage recording failed invocationId={InvocationId}");
    public async Task RecordAsync(AiUsageBatch batch, CancellationToken cancellationToken = default)
    {
        try { await PersistAsync(batch, cancellationToken); }
        catch (Exception) { RecordingFailed(logger, batch.Invocation.InvocationId, null); }
    }
    private async Task PersistAsync(AiUsageBatch batch, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var invocation = batch.Invocation;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ai_usage_invocations (invocation_id, user_id, tenant_id, started_at, duration, outcome, model_calls, tool_calls, successful_tool_calls)
            VALUES ({invocation.InvocationId}, {invocation.UserId}, {invocation.TenantId}, {invocation.StartedAt}, {invocation.Duration.Ticks}, {(int)invocation.Outcome},
                {invocation.ModelCalls}, {invocation.ToolCalls}, {invocation.SuccessfulToolCalls}) ON CONFLICT DO NOTHING
            """, cancellationToken);
        foreach (var record in batch.Records)
        {
            if (record.InvocationId != invocation.InvocationId) throw new InvalidOperationException("AI usage correlation mismatch.");
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ai_usage_records (id, invocation_id, model_call_id, attempt, provider, model, reported_model, started_at, duration,
                    outcome, input_tokens, output_tokens, reasoning_tokens, cached_tokens, provider_request_id, provider_response_id,
                    provider_cost, provider_cost_currency, billing_mode)
                VALUES ({record.Id}, {record.InvocationId}, {record.ModelCallId}, {record.Attempt}, {record.Provider}, {record.Model},
                    {record.ReportedModel}, {record.StartedAt}, {record.Duration.Ticks}, {(int)record.Outcome}, {record.InputTokens},
                    {record.OutputTokens}, {record.ReasoningTokens}, {record.CachedTokens}, {record.ProviderRequestId}, {record.ProviderResponseId},
                    {record.ProviderCost}, {record.ProviderCostCurrency}, {(int?)record.BillingMode}) ON CONFLICT DO NOTHING
                """, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
