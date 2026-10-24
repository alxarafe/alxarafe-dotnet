using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alxarafe.Modules.Ai.Application;
using Microsoft.Extensions.Logging;

namespace Alxarafe.Modules.Ai.Infrastructure;

// Shared wire format and single-completion transport. Provider policies may override
// CompleteRequestAsync; no provider retry policy is enabled by default.
public partial class OpenAiCompatibleModelClient(HttpClient http, string baseUrl, string model, string? apiKey,
    ILogger? logger = null, string? provider = null) : IAiModelClient
{
    private const string TabularResultInstructions = " List results may encode items as {columns,rows}: each row follows columns in order; fields in constants apply to every row. Empty rows still represent individual records. All values and pagination are preserved.";
    private const string ChatPath = "/chat/completions";
    private const string FunctionType = "function";
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken = default)
    {
        if (apiKey is not null && string.IsNullOrWhiteSpace(apiKey)) throw new AiModelFailureException();
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
        var completion = new CompletionContext(request);
        try
        {
            var result = await CompleteRequestAsync(payload, completion, cancellationToken);
            return result with { Executions = completion.Records.ToArray() };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { throw new AiModelCancelledException(FailureRecords(completion, AiExecutionOutcome.Cancelled), cancellationToken); }
        catch (OperationCanceledException)
        { throw new AiModelUnavailableException(executions: FailureRecords(completion, AiExecutionOutcome.TimedOut)); }
        catch (HttpRequestException)
        {
            Audit(request.InvocationId, 0, 0, "transport_unavailable");
            throw new AiModelUnavailableException(executions: FailureRecords(completion, AiExecutionOutcome.Failed));
        }
        catch (AiModelUnavailableException)
        { throw new AiModelUnavailableException(executions: FailureRecords(completion, AiExecutionOutcome.Failed)); }
        catch (AiModelFailureException)
        { throw new AiModelFailureException(executions: FailureRecords(completion, AiExecutionOutcome.Failed)); }
        catch (JsonException)
        { throw new AiModelFailureException(executions: FailureRecords(completion, AiExecutionOutcome.Failed)); }
    }

    protected sealed class CompletionContext(AiModelRequest request)
    {
        public AiModelRequest Request { get; } = request;
        public Guid ModelCallId { get; } = Guid.NewGuid();
        public List<AiUsageRecord> Records { get; } = [];
    }

    private static AiUsageRecord[] FailureRecords(CompletionContext completion, AiExecutionOutcome outcome)
    {
        if (completion.Records.Count > 0 && completion.Records[^1].Outcome == AiExecutionOutcome.Succeeded)
            completion.Records[^1] = completion.Records[^1] with { Outcome = outcome };
        return completion.Records.ToArray();
    }

    protected virtual async Task<AiModelResponse> CompleteRequestAsync(ChatRequest payload, CompletionContext completion, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(payload, completion, 1, cancellationToken);
        return await ReadResponseAsync(response, completion.Request.InvocationId, 1, cancellationToken);
    }

    protected async Task<HttpResponseMessage> SendAsync(ChatRequest payload, CompletionContext completion, int attempt, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + ChatPath);
        if (apiKey is not null) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = JsonContent.Create(payload, options: RequestJson);
        var record = new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            InvocationId = completion.Request.InvocationId,
            ModelCallId = completion.ModelCallId,
            Attempt = attempt,
            Provider = provider,
            Model = model,
            StartedAt = DateTimeOffset.UtcNow
        };
        long started = Stopwatch.GetTimestamp();
        HttpResponseMessage response;
        try { response = await http.SendAsync(message, ct); }
        catch (OperationCanceledException)
        {
            completion.Records.Add(record with
            {
                Duration = Stopwatch.GetElapsedTime(started),
                Outcome = ct.IsCancellationRequested ? AiExecutionOutcome.Cancelled : AiExecutionOutcome.TimedOut
            });
            throw;
        }
        catch (HttpRequestException)
        {
            completion.Records.Add(record with { Duration = Stopwatch.GetElapsedTime(started), Outcome = AiExecutionOutcome.Failed });
            throw;
        }
        record = record with
        {
            Duration = Stopwatch.GetElapsedTime(started),
            Outcome = response.IsSuccessStatusCode ? AiExecutionOutcome.Succeeded : AiExecutionOutcome.Failed
        };
        Audit(completion.Request.InvocationId, (int)response.StatusCode, attempt, "received");
        // SendAsync has buffered the complete body. Read only whitelisted metadata;
        // malformed/missing usage never changes the functional completion parser.
        JsonDocument? document = null;
        try { document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(CancellationToken.None)); }
        catch (JsonException) { }
        using (document)
        {
            JsonElement? body = document?.RootElement;
            var usage = ReadObject(body, "usage");
            completion.Records.Add(record with
            {
                ReportedModel = ReadText(body, "model"),
                InputTokens = Counter(usage, "prompt_tokens"),
                OutputTokens = Counter(usage, "completion_tokens"),
                ReasoningTokens = Counter(ReadObject(usage, "completion_tokens_details"), "reasoning_tokens"),
                CachedTokens = Counter(ReadObject(usage, "prompt_tokens_details"), "cached_tokens"),
                ProviderRequestId = ReadProviderRequestId(response, body),
                ProviderResponseId = ReadText(body, "id")
            });
        }
        return response;
    }

    private const string RequestIdHeader = "x-request-id";
    protected static JsonElement? ReadObject(JsonElement? parent, string field) =>
        parent is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(field, out var child)
            && child.ValueKind == JsonValueKind.Object ? child : null;
    private static long? Counter(JsonElement? parent, string field) =>
        parent is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(field, out var child)
            && child.ValueKind == JsonValueKind.Number && child.TryGetInt64(out long count) && count >= 0 ? count : null;
    private static string? MetadataIdentifier(string? value) =>
        value is { Length: > 0 and <= AiUsageRecord.MaximumIdentifierLength }
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or ':' or '/' or '.') ? value : null;
    protected static string? ReadText(JsonElement? parent, string field) =>
        parent is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(field, out var child)
            && child.ValueKind == JsonValueKind.String ? MetadataIdentifier(child.GetString()) : null;
    protected virtual string? ReadProviderRequestId(HttpResponseMessage response, JsonElement? body)
    {
        if (!response.Headers.TryGetValues(RequestIdHeader, out var values)) return null;
        var ids = values.Take(2).ToArray();
        return ids.Length == 1 ? MetadataIdentifier(ids[0]) : null;
    }

    protected async Task<AiModelResponse> ReadResponseAsync(HttpResponseMessage response, string? invocationId, int attempt, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            throw new AiModelUnavailableException();
        if (!response.IsSuccessStatusCode) throw new AiModelFailureException();
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);
        try { return Parse(body); }
        catch (AiModelFailureException) { Audit(invocationId, (int)response.StatusCode, attempt, "malformed_response"); throw; }
    }

    protected void Audit(string? id, int status, int attempt, string stage)
    {
        if (logger?.IsEnabled(LogLevel.Information) == true) LogCompletion(logger, id, status, attempt, stage);
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

    protected sealed record ChatRequest([property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("tools")] IReadOnlyList<ChatTool>? Tools,
        [property: JsonPropertyName("tool_choice")] string? ToolChoice,
        [property: JsonPropertyName("parallel_tool_calls")] bool? ParallelToolCalls,
        [property: JsonPropertyName("temperature")] double? Temperature = null);

    protected sealed record ChatMessage([property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<ChatToolCall>? ToolCalls = null,
        [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null);

    protected sealed record ChatTool([property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] ChatFunction Function);

    protected sealed record ChatFunction([property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] JsonElement Parameters);

    protected sealed record ChatToolCall([property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] ChatFunctionCall Function);

    protected sealed record ChatFunctionCall([property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] string Arguments);

    private sealed record ChatResponse([property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice([property: JsonPropertyName("message")] ChatResponseMessage? Message,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record ChatResponseMessage([property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<ChatResponseToolCall>? ToolCalls);

    private sealed record ChatResponseToolCall([property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("function")] ChatResponseFunction? Function);

    private sealed record ChatResponseFunction([property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("arguments")] string? Arguments);
}
