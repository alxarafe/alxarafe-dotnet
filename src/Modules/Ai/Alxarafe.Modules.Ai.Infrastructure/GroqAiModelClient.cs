using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alxarafe.Modules.Ai.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Alxarafe.Modules.Ai.Infrastructure;

public sealed class GroqAiModelClient : OpenAiCompatibleModelClient
{
    public const string ProviderName = "groq";
    public const string DefaultBaseUrl = "https://api.groq.com/openai/v1";
    public const string DefaultModel = "qwen/qwen3.8-27b";
    public const string ApiKeyConfiguration = "AI:Groq:ApiKey";
    public const string ModelConfiguration = "AI:Groq:Model"; // Legacy alias; AI:Model takes precedence.
    public const string BaseUrlConfiguration = "AI:Groq:BaseUrl";
    public const string TimeoutConfiguration = "AI:Groq:TimeoutSeconds";
    public const int DefaultTimeoutSeconds = 60;
    private const string ToolGenerationFailureCode = "tool_use_failed";
    private const int MaximumToolGenerationRetries = 1;
    private const int MaximumProviderRetries = 1;
    private const int MaximumRetryAfterSeconds = 60;
    private const double ToolRetryTemperature = 0;
    private readonly ILogger<GroqAiModelClient>? logger;

    public GroqAiModelClient(HttpClient http, IConfiguration configuration, ILogger<GroqAiModelClient>? logger = null)
        : base(http, configuration[BaseUrlConfiguration] ?? DefaultBaseUrl,
            configuration[AiProviderResolver.ModelConfiguration] ?? configuration[ModelConfiguration] ?? DefaultModel,
            configuration[ApiKeyConfiguration] ?? "", logger)
    {
        this.logger = logger;
        http.Timeout = TimeSpan.FromSeconds(configuration.GetValue(TimeoutConfiguration, DefaultTimeoutSeconds));
    }

    protected override async Task<AiModelResponse> CompleteRequestAsync(ChatRequest payload, AiModelRequest request, CancellationToken cancellationToken)
    {
        int generationRetries = 0, providerRetries = 0;
        for (int attempt = 0; attempt <= MaximumToolGenerationRetries + MaximumProviderRetries; attempt++)
        {
            using var response = await SendAsync(payload, request.InvocationId, attempt + 1, cancellationToken);
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
            return await ReadResponseAsync(response, request.InvocationId, attempt + 1, cancellationToken);
        }
        throw new AiModelFailureException();
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
    private sealed record ChatErrorResponse([property: JsonPropertyName("error")] ChatError? Error);

    private sealed record ChatError([property: JsonPropertyName("code")] string? Code, [property: JsonPropertyName("message")] string? Message);

}
