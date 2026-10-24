using Alxarafe.Modules.Ai.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Alxarafe.Modules.Ai.Infrastructure;

public static class AiProviderResolver
{
    public const string ProviderConfiguration = "AI:Provider";
    public const string ModelConfiguration = "AI:Model";

    public static IAiModelClient Resolve(HttpClient http, IConfiguration configuration, ILoggerFactory loggerFactory) =>
        (configuration[ProviderConfiguration] ?? GroqAiModelClient.ProviderName) switch
        {
            GroqAiModelClient.ProviderName => new GroqAiModelClient(http, configuration, loggerFactory.CreateLogger<GroqAiModelClient>()),
            OllamaAiModelClient.ProviderName => new OllamaAiModelClient(http, configuration, loggerFactory.CreateLogger<OllamaAiModelClient>()),
            _ => throw new InvalidOperationException("Unsupported " + ProviderConfiguration + ". Supported providers: " + GroqAiModelClient.ProviderName + ", " + OllamaAiModelClient.ProviderName + ".")
        };
}
