using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alxarafe.Modules.Ai.Application;
using Microsoft.Extensions.Configuration;

namespace Alxarafe.Modules.Ai.Infrastructure;

public sealed class GroqAiModelClient(HttpClient http, IConfiguration configuration) : IAiModelClient
{
    public const string DefaultBaseUrl = "https://api.groq.com/openai/v1";
    public const string DefaultModel = "qwen/qwen3.8-27b";
    public const string ApiKeyConfiguration = "AI:Groq:ApiKey";
    public const string ModelConfiguration = "AI:Groq:Model";
    public const string BaseUrlConfiguration = "AI:Groq:BaseUrl";
    public const string TimeoutConfiguration = "AI:Groq:TimeoutSeconds";
    public const int DefaultTimeoutSeconds = 60;
    private const string ChatPath = "/chat/completions";
    private const string FunctionType = "function";
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken = default)
    {
        string apiKey = configuration[ApiKeyConfiguration] ?? "";
        if (string.IsNullOrWhiteSpace(apiKey)) throw new AiModelFailureException();
        string baseUrl = configuration[BaseUrlConfiguration] ?? DefaultBaseUrl;
        string model = configuration[ModelConfiguration] ?? DefaultModel;
        var payload = new ChatRequest(model, Messages(request), Tools(request), request.Tools.Count == 0 ? null : "auto",
            request.Tools.Count == 0 ? null : false);
        using var message = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + ChatPath);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = JsonContent.Create(payload, options: RequestJson);
        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                throw new AiModelUnavailableException();
            if (!response.IsSuccessStatusCode) throw new AiModelFailureException();
            var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken);
            return Parse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new AiModelUnavailableException(); }
        catch (HttpRequestException) { throw new AiModelUnavailableException(); }
        catch (JsonException) { throw new AiModelFailureException(); }
    }

    private static List<ChatMessage> Messages(AiModelRequest request)
    {
        var messages = new List<ChatMessage> { new(AiRoles.System, request.SystemPrompt) };
        foreach (var message in request.Messages)
        {
            if (message.Role == AiRoles.Assistant && message.ToolCalls is { Count: > 0 })
                messages.Add(new ChatMessage(AiRoles.Assistant, message.Content,
                    message.ToolCalls.Select(call => new ChatToolCall(call.Id, FunctionType,
                        new ChatFunctionCall(call.Name, call.Arguments.GetRawText()))).ToArray()));
            else if (message.Role == AiRoles.Tool)
                messages.Add(new ChatMessage(AiRoles.Tool, message.Content, ToolCallId: message.ToolCallId));
            else
                messages.Add(new ChatMessage(message.Role, message.Content));
        }
        return messages;
    }

    private static ChatTool[]? Tools(AiModelRequest request) => request.Tools.Count == 0 ? null
        : request.Tools.Select(tool => new ChatTool(FunctionType,
            new ChatFunction(tool.Name, tool.Description, tool.InputSchema))).ToArray();

    private static AiModelResponse Parse(ChatResponse? body)
    {
        var message = body?.Choices is { Count: > 0 } choices ? choices[0].Message : null;
        if (message is null) throw new AiModelFailureException();
        if (message.ToolCalls is not { Count: > 0 })
        {
            if (string.IsNullOrWhiteSpace(message.Content)) throw new AiModelFailureException();
            return new AiModelResponse(message.Content, []);
        }
        var calls = new List<AiToolRequest>();
        foreach (var call in message.ToolCalls)
        {
            if (string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Function?.Name)
                || string.IsNullOrWhiteSpace(call.Function.Arguments)) throw new AiModelFailureException();
            JsonElement arguments;
            try
            {
                using var document = JsonDocument.Parse(call.Function.Arguments);
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new AiModelFailureException();
                arguments = document.RootElement.Clone();
            }
            catch (JsonException) { throw new AiModelFailureException(); }
            calls.Add(new AiToolRequest(call.Id, call.Function.Name, arguments));
        }
        return new AiModelResponse(message.Content, calls);
    }

    private sealed record ChatRequest([property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("tools")] IReadOnlyList<ChatTool>? Tools,
        [property: JsonPropertyName("tool_choice")] string? ToolChoice,
        [property: JsonPropertyName("parallel_tool_calls")] bool? ParallelToolCalls);

    private sealed record ChatMessage([property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<ChatToolCall>? ToolCalls = null,
        [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null);

    private sealed record ChatTool([property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] ChatFunction Function);

    private sealed record ChatFunction([property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] JsonElement Parameters);

    private sealed record ChatToolCall([property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] ChatFunctionCall Function);

    private sealed record ChatFunctionCall([property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] string Arguments);

    private sealed record ChatResponse([property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice([property: JsonPropertyName("message")] ChatResponseMessage? Message);

    private sealed record ChatResponseMessage([property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<ChatResponseToolCall>? ToolCalls);

    private sealed record ChatResponseToolCall([property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("function")] ChatResponseFunction? Function);

    private sealed record ChatResponseFunction([property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("arguments")] string? Arguments);
}
