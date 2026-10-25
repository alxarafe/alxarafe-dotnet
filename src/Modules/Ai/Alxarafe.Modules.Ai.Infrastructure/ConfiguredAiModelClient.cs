using Alxarafe.Modules.Ai.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Alxarafe.Modules.Ai.Infrastructure;

// Async settings lookup cannot run in a synchronous DI factory. This scoped bridge
// freezes the selection for all completions of one invocation, never across requests.
public sealed class ConfiguredAiModelClient(HttpClient http, IConfiguration configuration,
    EffectiveAiConfigurationResolver resolver, ILoggerFactory loggerFactory) : IAiModelClient, IDisposable
{
    private Task<IAiModelClient>? selected;

    public async Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken = default)
    {
        var client = await (selected ??= SelectAsync(cancellationToken));
        return await client.CompleteAsync(request, cancellationToken);
    }

    private async Task<IAiModelClient> SelectAsync(CancellationToken cancellationToken) =>
        AiProviderResolver.Resolve(http, configuration, loggerFactory, await resolver.ResolveAsync(cancellationToken));

    public void Dispose() => http.Dispose();
}
