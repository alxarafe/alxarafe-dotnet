using System.Net;
using System.Text.Json;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class AiUsageNormalizationTests
{
    private static readonly int[] RetryAttempts = [1, 2, 3];
    private static readonly AiExecutionOutcome[] RetryOutcomes = [AiExecutionOutcome.Failed, AiExecutionOutcome.Failed, AiExecutionOutcome.Succeeded];
    private const string Secret = "sensitive-content-and-credential";
    private const string Model = "requested-model";
    private const string Answer = "Safe final answer.";
    private const string Completion = """{"id":"completion-id","model":"reported-model","choices":[{"message":{"content":"Safe final answer.","reasoning":"sensitive-content-and-credential"}}]}""";
    private static readonly AiModelRequest Request = new(Secret, [new(AiRoles.User, Secret)],
        [new("read_tool", "read.fixture", "Read", "Read", JsonSerializer.SerializeToElement(new { type = "object" }))], "invocation-fixture");

    [Fact]
    public async Task GroqNormalizesOnlyReportedCountersAndSeparatesRequestFromResponseIdentity()
    {
        const string body = """{"id":"chatcmpl-fixture","model":"served-model","x_groq":{"id":"req-groq"},"usage":{"prompt_tokens":12,"completion_tokens":7,"completion_tokens_details":{"reasoning_tokens":3},"prompt_tokens_details":{"cached_tokens":4}},"choices":[{"message":{"content":"Safe final answer.","reasoning":"sensitive-content-and-credential"}}]}""";
        using var handler = new Wire((HttpStatusCode.OK, body));
        using var http = new HttpClient(handler);
        var result = await Client(GroqAiModelClient.ProviderName, http).CompleteAsync(Request);
        Assert.Equal(Answer, result.Answer);
        var usage = Assert.Single(result.Executions!);
        Assert.Equal(GroqAiModelClient.ProviderName, usage.Provider);
        Assert.Equal(Model, usage.Model);
        Assert.Equal("served-model", usage.ReportedModel);
        Assert.Equal(12, usage.InputTokens);
        Assert.Equal(7, usage.OutputTokens);
        Assert.Equal(3, usage.ReasoningTokens);
        Assert.Equal(4, usage.CachedTokens);
        Assert.Equal("req-groq", usage.ProviderRequestId);
        Assert.Equal("chatcmpl-fixture", usage.ProviderResponseId);
        Assert.True(usage.Duration >= TimeSpan.Zero);
        Assert.Equal(AiExecutionOutcome.Succeeded, usage.Outcome);
        Assert.Null(usage.ProviderCost);
        Assert.Null(usage.ProviderCostCurrency);
        Assert.Null(usage.BillingMode);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(usage));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3L)]
    public async Task OllamaMapsCompatibleUsageWithoutInventingReasoningOrPrice(long? reasoningTokens)
    {
        const string body = """{"id":"ollama-completion","usage":{"prompt_tokens":11,"completion_tokens":5,"prompt_tokens_details":{"cached_tokens":0},"cost":99},"choices":[{"message":{"content":"Safe final answer.","reasoning":"sensitive-content-and-credential"}}]}""";
        string completion = reasoningTokens is { } count
            ? body.Replace("\"completion_tokens\":5", "\"completion_tokens\":5,\"completion_tokens_details\":{\"reasoning_tokens\":" + count + "}", StringComparison.Ordinal)
            : body;
        using var handler = new Wire((HttpStatusCode.OK, completion));
        using var http = new HttpClient(handler);
        var result = await Client(OllamaAiModelClient.ProviderName, http).CompleteAsync(Request);
        var usage = Assert.Single(result.Executions!);
        Assert.Equal(OllamaAiModelClient.ProviderName, usage.Provider);
        Assert.Equal(11, usage.InputTokens);
        Assert.Equal(5, usage.OutputTokens);
        Assert.Equal(0, usage.CachedTokens);
        Assert.Equal(reasoningTokens, usage.ReasoningTokens);
        Assert.Null(usage.ProviderCost);
        Assert.Null(usage.ProviderCostCurrency);
        Assert.Null(usage.BillingMode);
        Assert.Null(handler.Authorization);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(usage));
    }

    [Theory]
    [InlineData(GroqAiModelClient.ProviderName, null)]
    [InlineData(OllamaAiModelClient.ProviderName, null)]
    [InlineData(GroqAiModelClient.ProviderName, "null")]
    [InlineData(OllamaAiModelClient.ProviderName, "null")]
    [InlineData(GroqAiModelClient.ProviderName, "{}")]
    [InlineData(OllamaAiModelClient.ProviderName, "{}")]
    [InlineData(GroqAiModelClient.ProviderName, "{\"prompt_tokens\":-1,\"completion_tokens\":1.5,\"prompt_tokens_details\":[],\"completion_tokens_details\":{\"reasoning_tokens\":\"unknown\"}}")]
    [InlineData(OllamaAiModelClient.ProviderName, "{\"prompt_tokens\":-1,\"completion_tokens\":1.5,\"prompt_tokens_details\":[],\"completion_tokens_details\":{\"reasoning_tokens\":\"unknown\"}}")]
    public async Task MissingOrMalformedMetricsStayUnknownWithoutChangingTheAnswer(string provider, string? metrics)
    {
        string body = metrics is null ? Completion
            : Completion.Replace("\"choices\":", "\"usage\":" + metrics + ",\"choices\":", StringComparison.Ordinal);
        using var handler = new Wire((HttpStatusCode.OK, body));
        using var http = new HttpClient(handler);
        var result = await Client(provider, http).CompleteAsync(Request);
        Assert.Equal(Answer, result.Answer);
        var usage = Assert.Single(result.Executions!);
        Assert.Null(usage.InputTokens);
        Assert.Null(usage.OutputTokens);
        Assert.Null(usage.ReasoningTokens);
        Assert.Null(usage.CachedTokens);
        Assert.Null(usage.ProviderCost);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(usage));
    }

    [Fact]
    public async Task InternalRetriesAreDistinctMeasuredRequestsNotRepeatedEvents()
    {
        using var handler = new Wire((HttpStatusCode.BadRequest, WithUsage("{\"error\":{\"code\":\"tool_use_failed\"}}", 10, 2)),
            (HttpStatusCode.TooManyRequests, WithUsage("{}", 20, 3)), (HttpStatusCode.OK, WithUsage(Completion, 30, 4)));
        using var http = new HttpClient(handler);
        var result = await Client(GroqAiModelClient.ProviderName, http).CompleteAsync(Request);
        var records = result.Executions!;
        Assert.Equal(3, records.Count);
        Assert.Equal(3, records.Select(record => record.Id).Distinct().Count());
        Assert.Single(records.Select(record => record.ModelCallId).Distinct());
        Assert.Equal(RetryAttempts, records.Select(record => record.Attempt));
        Assert.Equal(RetryOutcomes, records.Select(record => record.Outcome));
        var totals = AiUsageTotals.From(records.Concat(records));
        Assert.Equal(3, totals.ProviderAttempts);
        Assert.Equal(60, totals.InputTokens);
        Assert.Equal(9, totals.OutputTokens);
        Assert.Null(totals.CachedTokens);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ProviderErrorUsageSurvivesTheSafeFailure(HttpStatusCode status)
    {
        using var handler = new Wire((status, WithUsage("{}", 10, 2)));
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<AiModelFailureException>(() => Client(GroqAiModelClient.ProviderName, http).CompleteAsync(Request));
        var usage = Assert.Single(failure.Executions);
        Assert.Equal(10, usage.InputTokens);
        Assert.Equal(AiExecutionOutcome.Failed, usage.Outcome);
    }

    [Fact]
    public async Task UsageSurvivesAnUnusableCompletionAndInvalidJsonRemainsUnknown()
    {
        using var handler = new Wire((HttpStatusCode.OK, WithUsage("{}", 4, 1)), (HttpStatusCode.OK, "not-json"));
        using var http = new HttpClient(handler);
        var client = Client(OllamaAiModelClient.ProviderName, http);
        var invalidCompletion = await Assert.ThrowsAsync<AiModelFailureException>(() => client.CompleteAsync(Request));
        Assert.Equal(4, Assert.Single(invalidCompletion.Executions).InputTokens);
        Assert.Equal(AiExecutionOutcome.Failed, invalidCompletion.Executions[0].Outcome);
        var invalidJson = await Assert.ThrowsAsync<AiModelFailureException>(() => client.CompleteAsync(Request));
        Assert.Null(Assert.Single(invalidJson.Executions).InputTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutAndCallerCancellationRetainAttemptDurationWithoutGuessedTokens(bool callerCancellation)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new Wire((HttpStatusCode.OK, Completion))
        {
            BeforeResponse = () => { if (callerCancellation) cancellation.Cancel(); throw new TaskCanceledException(Secret); }
        };
        using var http = new HttpClient(handler);
        IReadOnlyList<AiUsageRecord> records;
        if (callerCancellation)
        {
            var failure = await Assert.ThrowsAsync<AiModelCancelledException>(() => Client(OllamaAiModelClient.ProviderName, http).CompleteAsync(Request, cancellation.Token));
            records = failure.Executions;
        }
        else
        {
            var failure = await Assert.ThrowsAsync<AiModelUnavailableException>(() => Client(OllamaAiModelClient.ProviderName, http).CompleteAsync(Request));
            records = failure.Executions;
        }
        var record = Assert.Single(records);
        Assert.Equal(callerCancellation ? AiExecutionOutcome.Cancelled : AiExecutionOutcome.TimedOut, record.Outcome);
        Assert.Null(record.InputTokens);
        Assert.True(record.Duration >= TimeSpan.Zero);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(record));
    }

    [Fact]
    public async Task MultipleModelCallsAggregateWithoutDeduplicatingRepeatedProviderIds()
    {
        using var handler = new Wire((HttpStatusCode.OK, WithUsage(Completion, 3, 2)), (HttpStatusCode.OK, WithUsage(Completion, 7, 5)));
        using var http = new HttpClient(handler);
        var client = Client(OllamaAiModelClient.ProviderName, http);
        var first = Assert.Single((await client.CompleteAsync(Request)).Executions!);
        var second = Assert.Single((await client.CompleteAsync(Request)).Executions!);
        Assert.NotEqual(first.ModelCallId, second.ModelCallId);
        Assert.Equal(first.ProviderResponseId, second.ProviderResponseId);
        var totals = AiUsageTotals.From([first, second]);
        Assert.Equal(2, totals.ProviderAttempts);
        Assert.Equal(10, totals.InputTokens);
        Assert.Equal(7, totals.OutputTokens);
        Assert.Null(AiUsageTotals.From([first, second with { InputTokens = null }]).InputTokens);
        Assert.Null(AiUsageTotals.From([first with { InputTokens = long.MaxValue }, second]).InputTokens);
    }

    private static string WithUsage(string body, int input, int output) =>
        body[..^1] + (body.Length > 2 ? "," : "") + "\"usage\":{\"prompt_tokens\":" + input + ",\"completion_tokens\":" + output + "}}";
    private static IAiModelClient Client(string provider, HttpClient http) => AiProviderResolver.Resolve(http,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [AiProviderResolver.ProviderConfiguration] = provider,
            [AiProviderResolver.ModelConfiguration] = Model,
            [GroqAiModelClient.ApiKeyConfiguration] = Secret
        }).Build(), NullLoggerFactory.Instance);

    private sealed class Wire(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int calls;
        public Action? BeforeResponse { get; init; }
        public string? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            BeforeResponse?.Invoke();
            var item = responses[calls++];
            var response = new HttpResponseMessage(item.Status) { Content = new StringContent(item.Body) };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            response.Headers.Add("x-request-id", "request-fixture");
            return Task.FromResult(response);
        }
    }
}
