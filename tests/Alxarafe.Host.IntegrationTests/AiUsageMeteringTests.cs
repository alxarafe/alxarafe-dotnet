using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Modularity;
using Alxarafe.Modules.Ai;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Persistence;
using Alxarafe.Modules.Wms;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class AiUsageMeteringTests(TenantApplicationFactory factory) : IClassFixture<TenantApplicationFactory>, IAsyncLifetime
{
    private static readonly string[] PublicResponseFields = ["answer", "invocationId", "toolsUsed"];
    private const string Password = "ai-usage-fixture-password";
    private const string Secret = "sensitive-prompt-answer-error-key";
    private const string Tool = "wms_uoms_list";
    private const string FinalAnswer = "Safe registered-state answer.";
    private readonly List<string> invocationIds = [];
    private Guid userId;
    private string token = "";
    private HttpClient bootstrap = null!;

    public async Task InitializeAsync()
    {
        bootstrap = factory.CreateClient();
        await factory.Services.GetRequiredService<ModuleStateService>().EnableAsync(AiModule.Id);
        await factory.Services.GetRequiredService<ModuleStateService>().EnableAsync(WmsModule.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserAdministration>().CreateAsync("ai-usage-" + Guid.NewGuid().ToString("N") + "@example.test", Password, true);
        userId = user!.Id;
        using var response = await bootstrap.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    public async Task DisposeAsync()
    {
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<AiUsageDbContext>>().CreateDbContextAsync();
        await db.UsageRecords.Where(row => row.InvocationId != null && invocationIds.Contains(row.InvocationId)).ExecuteDeleteAsync();
        await db.Invocations.Where(row => invocationIds.Contains(row.InvocationId)).ExecuteDeleteAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(row => row.Id == userId).ExecuteDeleteAsync();
        bootstrap.Dispose();
    }

    [Fact]
    public async Task ActualDispatchesAndMultipleModelCallsPersistWithoutCountingDefinitionsOrProposals()
    {
        int turns = 0;
        var capture = Capture();
        using var host = Host(request =>
        {
            Assert.True(request.Tools.Count > 2);
            int turn = ++turns;
            AiToolRequest[] calls = turn switch
            {
                1 => [new("invalid", "wms_items_list", JsonSerializer.SerializeToElement(new { sku = "INVALID SKU" }))],
                2 => [new("valid", Tool, JsonSerializer.SerializeToElement(new { }))],
                _ => []
            };
            return new(turn == 3 ? FinalAnswer : null, calls, [Record(turn)]);
        }, capture);
        using var client = host.CreateClient();
        using var response = await Ask(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(FinalAnswer, body.GetProperty("answer").GetString());
        Assert.Single(body.GetProperty("toolsUsed").EnumerateArray());
        Assert.Equal(PublicResponseFields, body.EnumerateObject().Select(property => property.Name).Order());
        var batch = Assert.Single(capture.Batches);
        Assert.Equal(3, batch.Invocation.ModelCalls);
        Assert.Equal(2, batch.Invocation.ToolCalls);
        Assert.Equal(1, batch.Invocation.SuccessfulToolCalls);
        Assert.Equal(userId, batch.Invocation.UserId);
        Assert.Equal(AiInvocationOutcome.Completed, batch.Invocation.Outcome);
        await using var db = await Context();
        var summary = await db.Invocations.SingleAsync(row => row.InvocationId == batch.Invocation.InvocationId);
        Assert.Equal(batch.Invocation.UserId, summary.UserId);
        Assert.Equal(batch.Invocation.Duration, summary.Duration);
        Assert.Equal(batch.Invocation.ToolCalls, summary.ToolCalls);
        Assert.Equal(batch.Invocation.ModelCalls, summary.ModelCalls);
        Assert.Equal(batch.Invocation.StartedAt.Ticks / TimeSpan.TicksPerMicrosecond, summary.StartedAt.Ticks / TimeSpan.TicksPerMicrosecond);
        var rows = await db.UsageRecords.Where(row => row.InvocationId == summary.InvocationId).ToArrayAsync();
        Assert.Equal(3, rows.Length);
        var totals = AiUsageTotals.From(rows);
        Assert.Equal(60, totals.InputTokens);
        Assert.Equal(6, totals.OutputTokens);
        Assert.Equal(TimeSpan.FromSeconds(3), totals.ProviderDuration);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(rows));
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(summary));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task MalformedModelProposalsDoNotCountAsDispatchedTools()
    {
        var capture = Capture();
        using var host = Host(_ => new(null, [new("bad", Tool, JsonSerializer.SerializeToElement(Secret))], [Record(1)]), capture);
        using var client = host.CreateClient();
        using var response = await Ask(client);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var batch = Assert.Single(capture.Batches);
        Assert.Equal(0, batch.Invocation.ToolCalls);
        Assert.Equal(1, batch.Invocation.ModelCalls);
        Assert.Equal(AiInvocationOutcome.Failed, batch.Invocation.Outcome);
    }

    [Fact]
    public async Task FailureAfterAReadPersistsReportedFailureUsageWithoutChangingThePublicError()
    {
        int turns = 0;
        var capture = Capture();
        using var host = Host(_ => ++turns == 1
            ? new(null, [new("read", Tool, JsonSerializer.SerializeToElement(new { }))], [Record(1)])
            : throw new AiModelUnavailableException(Secret, executions: [Record(2) with { Outcome = AiExecutionOutcome.Failed }]), capture);
        using var client = host.CreateClient();
        using var response = await Ask(client);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(Secret, await response.Content.ReadAsStringAsync());
        var batch = Assert.Single(capture.Batches);
        Assert.Equal(2, batch.Records.Count);
        Assert.Equal(1, batch.Invocation.ToolCalls);
        Assert.Equal(1, batch.Invocation.SuccessfulToolCalls);
        Assert.Equal(AiInvocationOutcome.Failed, batch.Invocation.Outcome);
        await using var db = await Context();
        Assert.Equal(2, await db.UsageRecords.CountAsync(row => row.InvocationId == batch.Invocation.InvocationId));
    }

    [Fact]
    public async Task SecondaryMeterFailureDoesNotReplaceTheFunctionalAnswer()
    {
        using var host = Host(_ => new(FinalAnswer, [], [Record(1)]), new ThrowingMeter());
        using var client = host.CreateClient();
        using var response = await Ask(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(FinalAnswer, body.GetProperty("answer").GetString());
        Assert.DoesNotContain(Secret, body.GetRawText());
    }

    [Fact]
    public async Task RepeatedAndConcurrentPersistenceIsIdempotentButDistinctAttemptsStillSum()
    {
        var meter = factory.Services.GetRequiredService<IAiUsageMeter>();
        string id = Guid.NewGuid().ToString("N"); invocationIds.Add(id);
        var summary = new AiInvocationUsage { InvocationId = id, UserId = userId, StartedAt = DateTimeOffset.UtcNow, Outcome = AiInvocationOutcome.Completed, ModelCalls = 1 };
        var first = Record(1) with { InvocationId = id };
        var second = Record(2) with { InvocationId = id, ModelCallId = first.ModelCallId, Attempt = 2, ProviderResponseId = first.ProviderResponseId };
        var batch = new AiUsageBatch(summary, [first, second]);
        await Task.WhenAll(meter.RecordAsync(batch), meter.RecordAsync(batch));
        await meter.RecordAsync(batch);
        await using var db = await Context();
        Assert.Equal(1, await db.Invocations.CountAsync(row => row.InvocationId == id));
        var rows = await db.UsageRecords.Where(row => row.InvocationId == id).ToArrayAsync();
        Assert.Equal(2, rows.Length);
        Assert.Equal(30, AiUsageTotals.From(rows).InputTokens);
        Assert.All(rows, row => Assert.Null(row.BillingMode));
    }

    [Theory]
    [InlineData(AiBillingMode.Managed)]
    [InlineData(AiBillingMode.Byok)]
    [InlineData(AiBillingMode.Local)]
    public async Task FutureBillingClassificationIsRepresentableWithoutRuntimeInference(AiBillingMode mode)
    {
        string id = Guid.NewGuid().ToString("N"); invocationIds.Add(id);
        var record = Record(1) with { InvocationId = id, BillingMode = mode };
        await factory.Services.GetRequiredService<IAiUsageMeter>().RecordAsync(new(
            new AiInvocationUsage { InvocationId = id, StartedAt = DateTimeOffset.UtcNow }, [record]));
        await using var db = await Context();
        var actual = await db.UsageRecords.SingleAsync(row => row.Id == record.Id);
        Assert.Equal(mode, actual.BillingMode);
        Assert.Null(actual.ProviderCost);
    }

    [Fact]
    public async Task PersistentMeterFailureLogsOnlyTheInvocationNotExceptionContent()
    {
        var logger = new FailureLogger();
        var meter = new EfAiUsageMeter(new BrokenFactory(), logger);
        await meter.RecordAsync(new(new AiInvocationUsage { InvocationId = "failure-fixture" }, []));
        Assert.Single(logger.Messages);
        Assert.Contains("failure-fixture", logger.Messages[0]);
        Assert.DoesNotContain(Secret, logger.Messages[0]);
    }

    private CapturingMeter Capture() => new(factory.Services.GetRequiredService<IAiUsageMeter>(), invocationIds);
    private Task<AiUsageDbContext> Context() => factory.Services.GetRequiredService<IDbContextFactory<AiUsageDbContext>>().CreateDbContextAsync();
    private WebApplicationFactory<Program> Host(Func<AiModelRequest, AiModelResponse> complete, IAiUsageMeter meter) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiModelClient>(); services.AddScoped<IAiModelClient>(_ => new Model(complete));
            services.RemoveAll<IAiUsageMeter>(); services.AddSingleton(meter);
        }));
    private async Task<HttpResponseMessage> Ask(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ai/assistant/invocations") { Content = JsonContent.Create(new { message = Secret }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
    private static AiUsageRecord Record(int sequence) => new()
    {
        Id = Guid.NewGuid(),
        ModelCallId = Guid.NewGuid(),
        Attempt = 1,
        Provider = "groq",
        Model = "fixture-model",
        StartedAt = DateTimeOffset.UtcNow,
        Duration = TimeSpan.FromSeconds(1),
        Outcome = AiExecutionOutcome.Succeeded,
        InputTokens = sequence * 10,
        OutputTokens = sequence,
        ProviderResponseId = "same-provider-response-id"
    };
    private sealed class Model(Func<AiModelRequest, AiModelResponse> complete) : IAiModelClient
    {
        public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken = default) => Task.FromResult(complete(request));
    }
    private sealed class CapturingMeter(IAiUsageMeter inner, List<string> ids) : IAiUsageMeter
    {
        public List<AiUsageBatch> Batches { get; } = [];
        public Task RecordAsync(AiUsageBatch batch, CancellationToken cancellationToken = default)
        {
            Batches.Add(batch); ids.Add(batch.Invocation.InvocationId);
            return inner.RecordAsync(batch, cancellationToken);
        }
    }
    private sealed class ThrowingMeter : IAiUsageMeter
    {
        public Task RecordAsync(AiUsageBatch batch, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Secret);
    }
    private sealed class BrokenFactory : IDbContextFactory<AiUsageDbContext>
    {
        public AiUsageDbContext CreateDbContext() => throw new InvalidOperationException(Secret);
    }
    private sealed class FailureLogger : ILogger<EfAiUsageMeter>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
