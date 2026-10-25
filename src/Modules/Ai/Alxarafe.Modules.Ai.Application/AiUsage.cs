namespace Alxarafe.Modules.Ai.Application;

public enum AiExecutionOutcome { Succeeded, Failed, Cancelled, TimedOut }
public enum AiInvocationOutcome { Completed, Failed, Cancelled }
// Representable, but unresolved until trusted ownership/deployment context exists.
public enum AiBillingMode { Managed, Byok, Local }

// One actual provider attempt. No prompts, answers, tool payloads or credentials.
public sealed record AiUsageRecord
{
    public const int MaximumIdentifierLength = 256;
    public Guid Id { get; init; }
    public string? InvocationId { get; init; }
    public Guid ModelCallId { get; init; }
    public int Attempt { get; init; }
    public string? Provider { get; init; }
    public required string Model { get; init; }
    public string? ReportedModel { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public AiExecutionOutcome Outcome { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? ReasoningTokens { get; init; }
    public long? CachedTokens { get; init; }
    public string? ProviderRequestId { get; init; }
    public string? ProviderResponseId { get; init; }
    public decimal? ProviderCost { get; init; }
    public string? ProviderCostCurrency { get; init; }
    public AiBillingMode? BillingMode { get; init; }
}

public sealed record AiInvocationUsage
{
    public required string InvocationId { get; init; }
    public Guid? UserId { get; init; }
    public string? TenantId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public AiInvocationOutcome Outcome { get; init; }
    public int ModelCalls { get; init; }
    public int ToolCalls { get; init; }
    public int SuccessfulToolCalls { get; init; }
}

public sealed record AiUsageBatch(AiInvocationUsage Invocation, IReadOnlyList<AiUsageRecord> Records);

public interface IAiUsageMeter
{
    Task RecordAsync(AiUsageBatch batch, CancellationToken cancellationToken = default);
}

// A total is known only if every distinct attempt reports that metric.
// Cached/reasoning counts are separate detail counters, not added to input/output.
public sealed record AiUsageTotals(int ProviderAttempts, long? InputTokens, long? OutputTokens,
    long? ReasoningTokens, long? CachedTokens, TimeSpan ProviderDuration)
{
    public static AiUsageTotals From(IEnumerable<AiUsageRecord> records)
    {
        var distinct = records.DistinctBy(record => record.Id).ToArray();
        return new(distinct.Length, Sum(record => record.InputTokens), Sum(record => record.OutputTokens),
            Sum(record => record.ReasoningTokens), Sum(record => record.CachedTokens),
            TimeSpan.FromTicks(distinct.Sum(record => record.Duration.Ticks)));
        long? Sum(Func<AiUsageRecord, long?> selector)
        {
            if (distinct.Length == 0 || distinct.Any(record => selector(record) is null)) return null;
            try { return distinct.Sum(record => selector(record)!.Value); }
            catch (OverflowException) { return null; }
        }
    }
}
