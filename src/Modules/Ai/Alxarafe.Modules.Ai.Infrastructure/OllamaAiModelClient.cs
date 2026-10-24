using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Alxarafe.Modules.Ai.Infrastructure;

public sealed class OllamaAiModelClient : OpenAiCompatibleModelClient
{
    public const string ProviderName = "ollama";
    public const string DefaultBaseUrl = "http://127.0.0.1:11434/v1";
    public const string DefaultModel = "gemma4:latest";
    public const string BaseUrlConfiguration = "AI:Ollama:BaseUrl";
    public const string TimeoutConfiguration = "AI:Ollama:TimeoutSeconds";
    public const int DefaultTimeoutSeconds = 1800;

    public OllamaAiModelClient(HttpClient http, IConfiguration configuration, ILogger<OllamaAiModelClient>? logger = null)
        : base(http, configuration[BaseUrlConfiguration] ?? DefaultBaseUrl,
            configuration[AiProviderResolver.ModelConfiguration] ?? DefaultModel, apiKey: null, logger)
    {
        http.Timeout = TimeSpan.FromSeconds(configuration.GetValue(TimeoutConfiguration, DefaultTimeoutSeconds));
    }
}
