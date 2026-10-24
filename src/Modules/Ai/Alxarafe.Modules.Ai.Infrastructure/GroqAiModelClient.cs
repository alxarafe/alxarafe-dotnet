using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alxarafe.Modules.Ai.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Alxarafe.Modules.Ai.Infrastructure;

public sealed partial class GroqAiModelClient(HttpClient http, IConfiguration configuration, ILogger<GroqAiModelClient>? logger = null) : IAiModelClient
{
    public const string DefaultBaseUrl = "https://api.groq.com/openai/v1";
    public const string DefaultModel = "qwen/qwen3.8-27b";
    public const string ApiKeyConfiguration = "AI:Groq:ApiKey";
    public const string ModelConfiguration = "AI:Groq:Model";
    public const string BaseUrlConfiguration = "AI:Groq:BaseUrl";
    public const string TimeoutConfiguration = "AI:Groq:TimeoutSeconds";
    public const int DefaultTimeoutSeconds = 60;
    private const string TabularResultInstructions = " List results may encode items as {columns,rows}: each row follows columns in order; fields in constants apply to every row. Empty rows still represent individual records. All values and pagination are preserved.";
    private const string ChatPath = "/chat/completions";
    private const string FunctionType = "function";
    private const string ToolGenerationFailureCode = "tool_use_failed";
    private const int MaximumToolGenerationRetries = 1;
    private const int MaximumProviderRetries = 1;
    private const int MaximumRetryAfterSeconds = 60;
    private const double ToolRetryTemperature = 0;
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
        if (logger is not null && logger.IsEnabled(LogLevel.Debug))
        {
            int payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, RequestJson).Length;
            int schemaBytes = JsonSerializer.SerializeToUtf8Bytes(payload.Tools, RequestJson).Length;
            int originalToolBytes = request.Messages.Where(message => message.Role == AiRoles.Tool).Sum(message => Encoding.UTF8.GetByteCount(message.Content ?? ""));
            int encodedToolBytes = payload.Messages.Where(message => message.Role == AiRoles.Tool).Sum(message => Encoding.UTF8.GetByteCount(message.Content ?? ""));
            LogRequestSize(logger, request.InvocationId, payloadBytes, payload.Messages.Count, schemaBytes, originalToolBytes, encodedToolBytes);
        }
        try
        {
            int generationRetries = 0, providerRetries = 0;
            for (int attempt = 0; attempt <= MaximumToolGenerationRetries + MaximumProviderRetries; attempt++)
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + ChatPath);
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                message.Content = JsonContent.Create(payload, options: RequestJson);
                using var response = await http.SendAsync(message, cancellationToken);
                Audit(request.InvocationId, (int)response.StatusCode, attempt + 1, "received");
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    await AuditFailureAsync(request.InvocationId, response, attempt + 1, cancellationToken);
                    TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
                    if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryDate) retryAfter = retryDate - DateTimeOffset.UtcNow;
                    if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                        && providerRetries < MaximumProviderRetries && retryAfter is { } delay
                        && delay >= TimeSpan.Zero && delay <= TimeSpan.FromSeconds(MaximumRetryAfterSeconds))
                    {
                        providerRetries++;
                        Audit(request.InvocationId, (int)response.StatusCode, attempt + 1, "provider_retry_after");
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }
                    throw new AiModelUnavailableException();
                }
                ChatErrorResponse? parsedError = null;
                bool errorRead = false;
                if (response.StatusCode == HttpStatusCode.BadRequest && request.Tools.Count > 0
                    && generationRetries < MaximumToolGenerationRetries)
                {
                    parsedError = await response.Content.ReadFromJsonAsync<ChatErrorResponse>(cancellationToken: cancellationToken);
                    errorRead = true;
                    if (parsedError?.Error?.Code == ToolGenerationFailureCode)
                    {
                        // Groq rejected the generated call before execution. Retry only this completion;
                        // previously completed CORE calls and their tool messages remain unchanged.
                        generationRetries++;
                        Audit(request.InvocationId, (int)response.StatusCode, attempt + 1, "tool_generation_retry");
                        payload = payload with { Temperature = ToolRetryTemperature };
                        continue;
                    }
                }
                if (!response.IsSuccessStatusCode)
                {
                    if (errorRead) AuditFailure(request.InvocationId, (int)response.StatusCode, attempt + 1, parsedError);
                    else await AuditFailureAsync(request.InvocationId, response, attempt + 1, cancellationToken);
                    throw new AiModelFailureException();
                }
                var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken);
                try { return Parse(body); }
                catch (AiModelFailureException) { Audit(request.InvocationId, (int)response.StatusCode, attempt + 1, "malformed_response"); throw; }
            }
            throw new AiModelFailureException();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new AiModelUnavailableException(); }
        catch (HttpRequestException) { Audit(request.InvocationId, 0, 0, "transport_unavailable"); throw new AiModelUnavailableException(); }
        catch (JsonException) { throw new AiModelFailureException(); }
    }

    private void Audit(string? id, int status, int attempt, string stage)
    {
        if (logger?.IsEnabled(LogLevel.Information) == true) LogCompletion(logger, id, status, attempt, stage);
    }
    private async Task AuditFailureAsync(string? id, HttpResponseMessage response, int attempt, CancellationToken ct)
    {
        if (logger?.IsEnabled(LogLevel.Information) != true) return;
        try
        {
            var failure = await response.Content.ReadFromJsonAsync<ChatErrorResponse>(cancellationToken: ct);
            AuditFailure(id, (int)response.StatusCode, attempt, failure);
        }
        catch (JsonException) { Audit(id, (int)response.StatusCode, attempt, "upstream_non_json"); }
    }
    private void AuditFailure(string? id, int status, int attempt, ChatErrorResponse? failure)
    {
        // Only fixed categories are logged, never upstream messages or bodies.
        string message = failure?.Error?.Message ?? "";
        string stage = message.Contains("tokens per minute", StringComparison.OrdinalIgnoreCase) ? "token_rate_limit"
            : message.Contains("tokens per day", StringComparison.OrdinalIgnoreCase) ? "daily_token_limit"
            : message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ? "rate_limit"
            : failure?.Error?.Code == ToolGenerationFailureCode ? "tool_generation_failed" : "upstream_failure";
        Audit(id, status, attempt, stage);
    }
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "AI provider completion aiInvocationId={InvocationId} status={Status} attempt={Attempt} stage={Stage}")]
    private static partial void LogCompletion(ILogger logger, string? invocationId, int status, int attempt, string stage);

    private static List<ChatMessage> Messages(AiModelRequest request)
    {
        var messages = new List<ChatMessage> { new(AiRoles.System, request.SystemPrompt + TabularResultInstructions) };
        foreach (var message in request.Messages)
        {
            if (message.Role == AiRoles.Assistant && message.ToolCalls is { Count: > 0 })
                messages.Add(new ChatMessage(AiRoles.Assistant, message.Content,
                    message.ToolCalls.Select(call => new ChatToolCall(call.Id, FunctionType,
                        new ChatFunctionCall(call.Name, call.Arguments.GetRawText()))).ToArray()));
            else if (message.Role == AiRoles.Tool)
                messages.Add(new ChatMessage(AiRoles.Tool, CompactToolResult(message.Content), ToolCallId: message.ToolCallId));
            else
                messages.Add(new ChatMessage(message.Role, message.Content));
        }
        return messages;
    }

    // Lossless provider representation of uniform tabular pages: do not repeat column
    // names for every row. Application results and public contracts remain unchanged.
    private static string? CompactToolResult(string? content)
    {
        if (content is null) return null;
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() < 2
                || items[0].ValueKind != JsonValueKind.Object) return content;
            string[] columns = items[0].EnumerateObject().Select(property => property.Name).ToArray();
            if (columns.Length == 0 || columns.Distinct(StringComparer.Ordinal).Count() != columns.Length) return content;
            var records = items.EnumerateArray().ToArray();
            if (records.Any(item => item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != columns.Length
                || item.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != columns.Length
                || columns.Any(column => !item.TryGetProperty(column, out _)))) return content;
            var constants = columns.Where(column => records.All(item => item.GetProperty(column).GetRawText() == records[0].GetProperty(column).GetRawText()))
                .ToDictionary(column => column, column => records[0].GetProperty(column).Clone(), StringComparer.Ordinal);
            columns = columns.Where(column => !constants.ContainsKey(column)).ToArray();
            var rows = records.Select(item => columns.Select(column => item.GetProperty(column).Clone()).ToArray()).ToArray();
            var page = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!page.TryAdd(property.Name, property.Name == "items" ? new { columns, rows, constants } : property.Value.Clone())) return content;
            string compact = JsonSerializer.Serialize(page, RequestJson);
            return Encoding.UTF8.GetByteCount(compact) < Encoding.UTF8.GetByteCount(content) ? compact : content;
        }
        catch (JsonException) { return content; }
    }

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "AI request size aiInvocationId={InvocationId} payloadBytes={PayloadBytes} messages={Messages} schemaBytes={SchemaBytes} originalToolBytes={OriginalToolBytes} encodedToolBytes={EncodedToolBytes}")]
    private static partial void LogRequestSize(ILogger logger, string? invocationId, int payloadBytes, int messages,
        int schemaBytes, int originalToolBytes, int encodedToolBytes);

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
        [property: JsonPropertyName("parallel_tool_calls")] bool? ParallelToolCalls,
        [property: JsonPropertyName("temperature")] double? Temperature = null);

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

    private sealed record ChatErrorResponse([property: JsonPropertyName("error")] ChatError? Error);

    private sealed record ChatError([property: JsonPropertyName("code")] string? Code, [property: JsonPropertyName("message")] string? Message);

    private sealed record ChatResponse([property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice([property: JsonPropertyName("message")] ChatResponseMessage? Message);

    private sealed record ChatResponseMessage([property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<ChatResponseToolCall>? ToolCalls);

    private sealed record ChatResponseToolCall([property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("function")] ChatResponseFunction? Function);

    private sealed record ChatResponseFunction([property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("arguments")] string? Arguments);
}
