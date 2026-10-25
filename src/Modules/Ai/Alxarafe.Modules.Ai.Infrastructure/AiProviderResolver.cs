using Alxarafe.Modules.Ai.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Alxarafe.Modules.Ai.Infrastructure;

public static class AiProviderResolver
{
    public const string ProviderConfiguration = "AI:Provider";
    public const string ModelConfiguration = "AI:Model";

    public static void ValidateProvider(string provider)
    {
        if (provider is not GroqAiModelClient.ProviderName and not OllamaAiModelClient.ProviderName)
            throw new InvalidOperationException("Unsupported " + ProviderConfiguration + ". Supported providers: " + GroqAiModelClient.ProviderName + ", " + OllamaAiModelClient.ProviderName + ".");
    }

    public static IAiModelClient Resolve(HttpClient http, IConfiguration configuration, ILoggerFactory loggerFactory) =>
        Resolve(http, configuration, loggerFactory, EffectiveAiConfigurationResolver.Global(configuration));

    public static IAiModelClient Resolve(HttpClient http, IConfiguration configuration, ILoggerFactory loggerFactory,
        EffectiveAiConfiguration effective)
    {
        ValidateProvider(effective.ProviderId);
        // Overlay only the selected model. Credentials, URLs and timeouts remain platform-owned.
        using var selected = new ConfigurationManager();
        selected.AddConfiguration(configuration);
        selected.AddInMemoryCollection(new Dictionary<string, string?> { [ModelConfiguration] = effective.ModelId });
        return effective.ProviderId switch
        {
            GroqAiModelClient.ProviderName => new GroqAiModelClient(http, selected, loggerFactory.CreateLogger<GroqAiModelClient>()),
            _ => new OllamaAiModelClient(http, selected, loggerFactory.CreateLogger<OllamaAiModelClient>())
        };
    }
}
