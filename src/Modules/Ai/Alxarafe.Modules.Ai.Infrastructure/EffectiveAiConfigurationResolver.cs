using Alxarafe.Core;
using Microsoft.Extensions.Configuration;

namespace Alxarafe.Modules.Ai.Infrastructure;

public sealed record EffectiveAiConfiguration(string ProviderId, string ModelId);

// Platform settings select infrastructure only; they never select tools or permissions.
public sealed class EffectiveAiConfigurationResolver(IConfiguration configuration, ICurrentTenantContext tenant,
    ITenantAiSettingsStore settings)
{
    public async Task<EffectiveAiConfiguration> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is not { } tenantId) return Global(configuration);
        var overrides = await settings.FindAsync(tenantId, cancellationToken);
        var provider = string.IsNullOrWhiteSpace(overrides?.ProviderOverride) ? GlobalProvider(configuration) : overrides.ProviderOverride;
        AiProviderResolver.ValidateProvider(provider);
        var model = string.IsNullOrWhiteSpace(overrides?.ModelOverride) ? GlobalModel(configuration) : overrides.ModelOverride;
        return new(provider, model);
    }

    public static EffectiveAiConfiguration Global(IConfiguration configuration)
    {
        string provider = GlobalProvider(configuration);
        AiProviderResolver.ValidateProvider(provider);
        return new(provider, GlobalModel(configuration));
    }

    private static string GlobalProvider(IConfiguration configuration) =>
        configuration[AiProviderResolver.ProviderConfiguration] ?? GroqAiModelClient.ProviderName;

    private static string GlobalModel(IConfiguration configuration)
    {
        if (configuration[AiProviderResolver.ModelConfiguration] is { } model) return model;
        string provider = GlobalProvider(configuration);
        AiProviderResolver.ValidateProvider(provider);
        return provider == GroqAiModelClient.ProviderName
            ? configuration[GroqAiModelClient.ModelConfiguration] ?? GroqAiModelClient.DefaultModel
            : OllamaAiModelClient.DefaultModel;
    }
}
