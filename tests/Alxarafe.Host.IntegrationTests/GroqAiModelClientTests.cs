using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class GroqAiModelClientTests
{
    private static readonly string[] FixtureTags = ["one", "two"];
    private const string ToolName = "capability_2";
    private const string CapabilityId = "wms.uoms.list";
    private const string ToolCallId = "call-uoms";
    private const string FinalAnswer = "Two units are registered.";
    private const string GenerationFailure = """
        {"error":{"code":"tool_use_failed","message":"attempted to call an unknown tool","failed_generation":"sensitive-provider-output"}}
        """;
    private const string ToolCompletion = $$$"""
        {"choices":[{"finish_reason":"tool_calls","message":{"tool_calls":[{"id":"{{{ToolCallId}}}","type":"function","function":{"name":"{{{ToolName}}}","arguments":"{}"}}]}}]}
        """;
    private const string FinalCompletion = $$$"""
        {"choices":[{"finish_reason":"stop","message":{"content":"{{{FinalAnswer}}}"}}]}
        """;
    private static readonly JsonElement EmptyArguments = JsonSerializer.SerializeToElement(new { });
    private static readonly AiToolDefinition Tool = new(ToolName, CapabilityId, "List units of measure",
        "List registered units of measure", JsonSerializer.SerializeToElement(new { type = "object" }));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedToolGenerationRetriesOnlyTheCurrentCompletion(bool afterToolExecution)
    {
        using var handler = new ProviderHandler((HttpStatusCode.BadRequest, GenerationFailure),
            (HttpStatusCode.OK, afterToolExecution ? FinalCompletion : ToolCompletion));
        using var http = new HttpClient(handler);
        var request = Request(afterToolExecution);

        var result = await Client(http).CompleteAsync(request);

        Assert.Equal(2, handler.Requests.Count);
        var original = handler.Requests[0];
        var retry = handler.Requests[1];
        Assert.False(original.TryGetProperty("temperature", out _));
        Assert.Equal(0, retry.GetProperty("temperature").GetDouble());
        foreach (string property in new[] { "model", "messages", "tools", "tool_choice", "parallel_tool_calls" })
            Assert.Equal(original.GetProperty(property).GetRawText(), retry.GetProperty(property).GetRawText());
        foreach (var message in retry.GetProperty("messages").EnumerateArray())
        {
            Assert.False(message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Null);
            Assert.False(message.TryGetProperty("tool_call_id", out var id) && id.ValueKind == JsonValueKind.Null);
        }
        if (afterToolExecution)
        {
            Assert.Empty(result.ToolCalls);
            Assert.Equal(FinalAnswer, result.Answer);
            var messages = retry.GetProperty("messages").EnumerateArray().ToArray();
            Assert.Equal(ToolCallId, messages[^1].GetProperty("tool_call_id").GetString());
            Assert.Equal(ToolCallId, messages[^2].GetProperty("tool_calls")[0].GetProperty("id").GetString());
            Assert.Equal(request.Messages[^1].Content, messages[^1].GetProperty("content").GetString());
        }
        else
        {
            var call = Assert.Single(result.ToolCalls);
            Assert.Equal(ToolName, call.Name);
            Assert.Equal(ToolCallId, call.Id);
            Assert.Equal(JsonValueKind.Object, call.Arguments.ValueKind);
        }
    }

    [Fact]
    public async Task RepeatedToolGenerationFailureStopsAfterOneRetryWithoutLeakingProviderData()
    {
        using var handler = new ProviderHandler((HttpStatusCode.BadRequest, GenerationFailure),
            (HttpStatusCode.BadRequest, GenerationFailure));
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AiModelFailureException>(() => Client(http).CompleteAsync(Request()));
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain("sensitive-provider-output", error.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"invalid_schema\"}}", true)]
    [InlineData(HttpStatusCode.BadRequest, "not-json", true)]
    [InlineData(HttpStatusCode.BadRequest, GenerationFailure, false)]
    [InlineData(HttpStatusCode.Unauthorized, GenerationFailure, true)]
    public async Task OtherFailuresAreNotRetried(HttpStatusCode status, string body, bool hasTools)
    {
        using var handler = new ProviderHandler((status, body));
        using var http = new HttpClient(handler);
        var request = Request() with { Tools = hasTools ? [Tool] : [] };
        await Assert.ThrowsAsync<AiModelFailureException>(() => Client(http).CompleteAsync(request));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CancellationPreventsRetry()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new ProviderHandler((HttpStatusCode.BadRequest, GenerationFailure))
        {
            AfterResponse = cancellation.Cancel
        };
        using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(http).CompleteAsync(Request(), cancellation.Token));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitRetryAfterRetriesOnlyTheCurrentCompletionOnce(bool repeatedFailure)
    {
        using var handler = new ProviderHandler((HttpStatusCode.TooManyRequests, "{}"),
            repeatedFailure ? (HttpStatusCode.TooManyRequests, "{}") : (HttpStatusCode.OK, FinalCompletion))
        { RetryAfter = TimeSpan.Zero };
        using var http = new HttpClient(handler);
        if (repeatedFailure) await Assert.ThrowsAsync<AiModelUnavailableException>(() => Client(http).CompleteAsync(Request(true)));
        else Assert.Equal(FinalAnswer, (await Client(http).CompleteAsync(Request(true))).Answer);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].GetRawText(), handler.Requests[1].GetRawText());
    }

    [Fact]
    public async Task TabularToolEncodingPreservesEveryRecordValueAndPageMetadata()
    {
        var page = JsonSerializer.SerializeToElement(new
        {
            items = Enumerable.Range(0, 40).Select(index => new
            {
                warehouseCode = "REGISTERED-WAREHOUSE",
                sku = "ITEM-" + index,
                locationCode = "A.01.01",
                quantity = 1.25m + index,
                uom = "UN",
                occurredAt = "2026-06-20T10:00:00+00:00",
                description = "Descripción con acentos",
                optional = (string?)null,
                extra = new { tags = FixtureTags }
            }).ToArray(),
            offset = 0,
            limit = 100,
            total = 40,
            order = new[] { new { field = "sku", direction = "asc" } }
        });
        using var handler = new ProviderHandler((HttpStatusCode.OK, FinalCompletion));
        using var http = new HttpClient(handler);
        var request = Request(true);
        request = request with
        {
            Messages = request.Messages.Take(request.Messages.Count - 1)
            .Append(new AiModelMessage(AiRoles.Tool, page.GetRawText(), ToolCallId: ToolCallId)).ToArray()
        };
        await Client(http).CompleteAsync(request);
        var messages = handler.Requests.Single().GetProperty("messages");
        Assert.Equal(request.Messages.Count + 1, messages.GetArrayLength());
        string encoded = messages[messages.GetArrayLength() - 1].GetProperty("content").GetString()!;
        var compact = JsonSerializer.Deserialize<JsonElement>(encoded);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(encoded) < System.Text.Encoding.UTF8.GetByteCount(page.GetRawText()));
        var columns = compact.GetProperty("items").GetProperty("columns").EnumerateArray().Select(column => column.GetString()!).ToArray();
        var rows = compact.GetProperty("items").GetProperty("rows");
        Assert.Equal(page.GetProperty("items").GetArrayLength(), rows.GetArrayLength());
        var constants = compact.GetProperty("items").GetProperty("constants");
        Assert.Equal(page.GetProperty("items")[0].EnumerateObject().Count(), columns.Length + constants.EnumerateObject().Count());
        for (int index = 0; index < rows.GetArrayLength(); index++)
            foreach (var property in page.GetProperty("items")[index].EnumerateObject())
            {
                int column = Array.IndexOf(columns, property.Name);
                var value = column >= 0 ? rows[index][column] : constants.GetProperty(property.Name);
                Assert.Equal(property.Value.GetRawText(), value.GetRawText());
            }
        foreach (var property in page.EnumerateObject().Where(property => property.Name != "items"))
            Assert.Equal(property.Value.GetRawText(), compact.GetProperty(property.Name).GetRawText());
    }

    [Fact]
    public async Task ConstantColumnsDoNotDeduplicateIdenticalRecords()
    {
        var page = JsonSerializer.SerializeToElement(new
        {
            items = Enumerable.Repeat(new { sku = "REGISTERED-REFERENCE", quantity = 1.25m }, 3).ToArray(),
            total = 3
        });
        using var handler = new ProviderHandler((HttpStatusCode.OK, FinalCompletion));
        using var http = new HttpClient(handler);
        var request = Request(true);
        request = request with
        {
            Messages = request.Messages.Take(request.Messages.Count - 1)
            .Append(new AiModelMessage(AiRoles.Tool, page.GetRawText(), ToolCallId: ToolCallId)).ToArray()
        };
        await Client(http).CompleteAsync(request);
        var messages = handler.Requests.Single().GetProperty("messages");
        var compact = JsonSerializer.Deserialize<JsonElement>(messages[messages.GetArrayLength() - 1].GetProperty("content").GetString()!);
        Assert.Equal(3, compact.GetProperty("items").GetProperty("rows").GetArrayLength());
        Assert.Empty(compact.GetProperty("items").GetProperty("columns").EnumerateArray());
        Assert.Equal(1.25m, compact.GetProperty("items").GetProperty("constants").GetProperty("quantity").GetDecimal());
    }

    [Theory]
    [InlineData("{\"items\":[{\"a\":1},{\"b\":2}],\"total\":2}")]
    [InlineData("{\"items\":[]}")]
    [InlineData("{\"items\":[{\"a\":1}]}")]
    [InlineData("{\"items\":[{\"a\":1},{\"a\":2,\"a\":3}]}")]
    [InlineData("not-json")]
    public async Task NonUniformOrNonTabularToolResultsArePreservedVerbatim(string content)
    {
        using var handler = new ProviderHandler((HttpStatusCode.OK, FinalCompletion));
        using var http = new HttpClient(handler);
        var request = Request(true);
        request = request with
        {
            Messages = request.Messages.Take(request.Messages.Count - 1)
            .Append(new AiModelMessage(AiRoles.Tool, content, ToolCallId: ToolCallId)).ToArray()
        };
        await Client(http).CompleteAsync(request);
        var messages = handler.Requests.Single().GetProperty("messages");
        Assert.Equal(content, messages[messages.GetArrayLength() - 1].GetProperty("content").GetString());
    }

    private static GroqAiModelClient Client(HttpClient http) => new(http,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [GroqAiModelClient.ApiKeyConfiguration] = "synthetic-key"
        }).Build());

    private static AiModelRequest Request(bool afterToolExecution = false)
    {
        var messages = new List<AiModelMessage> { new(AiRoles.User, "List registered units of measure.") };
        if (afterToolExecution)
        {
            messages.Add(new(AiRoles.Assistant, null, [new(ToolCallId, ToolName, EmptyArguments)]));
            messages.Add(new(AiRoles.Tool, "{\"items\":[]}", ToolCallId: ToolCallId));
        }
        return new AiModelRequest("Synthetic system instructions.", messages, [Tool]);
    }

    private sealed class ProviderHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = [];
        public TimeSpan? RetryAfter { get; init; }
        public Action? AfterResponse { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken));
            var response = responses[Requests.Count - 1];
            AfterResponse?.Invoke();
            var message = new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(response.Body, System.Text.Encoding.UTF8, "application/json")
            };
            if (RetryAfter is { } delay) message.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
            return message;
        }
    }
}
