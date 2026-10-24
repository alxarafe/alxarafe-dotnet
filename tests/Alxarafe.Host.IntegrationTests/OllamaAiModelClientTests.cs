using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class OllamaAiModelClientTests
{
    private const string ToolName = "wms_inventory_list";
    private const string ToolCallId = "inventory-call";
    private const string FinalAnswer = "Registered balance read.";
    private const string FinalCompletion = """{"choices":[{"finish_reason":"stop","message":{"content":"Registered balance read.","reasoning":"private-provider-reasoning"}}]}""";
    private static readonly AiModelRequest Request = new("Synthetic instructions", [new(AiRoles.User, "Hello")], []);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalProviderUsesConfiguredModelAndUrlWithoutAnyApiKey(bool customConfiguration)
    {
        const string customUrl = "http://ollama.example.test:11434/v1/";
        const string customModel = "independently-configured-model";
        const int customTimeoutSeconds = 17;
        using var handler = new SyntheticHandler(FinalCompletion);
        using var http = new HttpClient(handler);
        var values = new Dictionary<string, string?>();
        if (customConfiguration)
        {
            values[OllamaAiModelClient.BaseUrlConfiguration] = customUrl;
            values[AiProviderResolver.ModelConfiguration] = customModel;
            values[OllamaAiModelClient.TimeoutConfiguration] = customTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var client = new OllamaAiModelClient(http, new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        var result = await client.CompleteAsync(Request);
        Assert.Equal(FinalAnswer, result.Answer);
        Assert.Empty(result.ToolCalls);
        Assert.Null(handler.Authorization);
        Assert.Equal((customConfiguration ? customUrl.TrimEnd('/') : OllamaAiModelClient.DefaultBaseUrl) + "/chat/completions", handler.Uri!.AbsoluteUri);
        Assert.Equal(customConfiguration ? customModel : OllamaAiModelClient.DefaultModel, handler.Payload.GetProperty("model").GetString());
        Assert.Equal(TimeSpan.FromSeconds(customConfiguration ? customTimeoutSeconds : OllamaAiModelClient.DefaultTimeoutSeconds), http.Timeout);
    }

    [Fact]
    public async Task ToolResultsAndStructuredCallsReuseTheCommonProtocolWithoutReasoning()
    {
        const string completion = """{"choices":[{"finish_reason":"tool_calls","message":{"reasoning":"private-provider-reasoning","tool_calls":[{"id":"next-call","type":"function","function":{"name":"wms_inventory_list","arguments":"{}"}}]}}]}""";
        using var handler = new SyntheticHandler(completion);
        using var http = new HttpClient(handler);
        var client = new OllamaAiModelClient(http, new ConfigurationBuilder().Build());
        var arguments = JsonSerializer.SerializeToElement(new { });
        var request = Request with
        {
            Tools = [new(ToolName, "", "Inventory", "Read registered inventory", JsonSerializer.SerializeToElement(new { type = "object" }), "wms.inventory.list")],
            Messages = [.. Request.Messages, new(AiRoles.Assistant, null, [new(ToolCallId, ToolName, arguments)]), new(AiRoles.Tool, "{\"items\":[]}", ToolCallId: ToolCallId)]
        };
        var result = await client.CompleteAsync(request);
        Assert.Null(result.Answer);
        Assert.Equal(ToolName, Assert.Single(result.ToolCalls).Name);
        var messages = handler.Payload.GetProperty("messages");
        Assert.Equal(ToolCallId, messages[messages.GetArrayLength() - 1].GetProperty("tool_call_id").GetString());
        Assert.Equal(request.Messages[^1].Content, messages[messages.GetArrayLength() - 1].GetProperty("content").GetString());
        Assert.Null(handler.Authorization);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GroqStillRejectsMissingOrBlankCredentialsBeforeTransport(string? key)
    {
        using var handler = new SyntheticHandler(FinalCompletion);
        using var http = new HttpClient(handler);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [GroqAiModelClient.ApiKeyConfiguration] = key }).Build();
        await Assert.ThrowsAsync<AiModelFailureException>(() => new GroqAiModelClient(http, configuration).CompleteAsync(Request));
        Assert.Equal(0, handler.Calls);
    }

    private sealed class SyntheticHandler(string body) : HttpMessageHandler
    {
        public JsonElement Payload { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Payload = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
