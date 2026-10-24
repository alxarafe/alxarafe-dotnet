using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class OpenAiCompatibleModelClientTests
{
    private const string SyntheticKey = "synthetic-key";
    private const string Model = "independent-model";
    private const string FinalCompletion = """{"choices":[{"finish_reason":"stop","message":{"content":"Safe final answer","reasoning":"private-reasoning"}}]}""";
    private static readonly AiModelRequest Request = new("Synthetic system", [new(AiRoles.User, "Hello")], []);

    [Theory]
    [InlineData(null)]
    [InlineData("legacy-model")]
    public async Task LegacyModelConfigurationAndDefaultRemainSupported(string? legacyModel)
    {
        using var handler = new SyntheticHandler(HttpStatusCode.OK, FinalCompletion);
        using var http = new HttpClient(handler);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [GroqAiModelClient.ApiKeyConfiguration] = SyntheticKey,
            [GroqAiModelClient.ModelConfiguration] = legacyModel
        }).Build();
        Assert.Equal("Safe final answer", (await new GroqAiModelClient(http, configuration).CompleteAsync(Request)).Answer);
        Assert.Equal(legacyModel ?? GroqAiModelClient.DefaultModel, handler.Payload.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task CommonProtocolDoesNotApplyGroqRetries(HttpStatusCode status)
    {
        using var handler = new SyntheticHandler(status, """{"error":{"code":"tool_use_failed"}}""");
        using var http = new HttpClient(handler);
        var client = new OpenAiCompatibleModelClient(http, "https://synthetic.example.test/v1", Model, SyntheticKey);
        var request = Request with { Tools = [new("synthetic_tool", "synthetic.read", "Read", "Synthetic", JsonSerializer.SerializeToElement(new { type = "object" }))] };
        if (status == HttpStatusCode.BadRequest) await Assert.ThrowsAsync<AiModelFailureException>(() => client.CompleteAsync(request));
        else await Assert.ThrowsAsync<AiModelUnavailableException>(() => client.CompleteAsync(request));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CommonProtocolIgnoresUnknownReasoningAndMetadataAndOmitsToolsWhenAbsent()
    {
        using var handler = new SyntheticHandler(HttpStatusCode.OK, FinalCompletion);
        using var http = new HttpClient(handler);
        var client = new OpenAiCompatibleModelClient(http, "https://synthetic.example.test/v1", Model, SyntheticKey);
        var result = await client.CompleteAsync(Request);
        Assert.Equal("Safe final answer", result.Answer);
        Assert.Empty(result.ToolCalls);
        Assert.Equal(Model, handler.Payload.GetProperty("model").GetString());
        Assert.False(handler.Payload.TryGetProperty("tools", out _));
        Assert.False(handler.Payload.TryGetProperty("tool_choice", out _));
        Assert.False(handler.Payload.TryGetProperty("parallel_tool_calls", out _));
    }

    private sealed class SyntheticHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public JsonElement Payload { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Payload = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return response;
        }
    }
}
