using Alxarafe.Modules.Ai;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class AiProviderResolutionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(GroqAiModelClient.ProviderName)]
    public void GroqAndTheLegacyDefaultResolveThroughTheRegisteredModelPort(string? provider)
    {
        using var host = Host(provider);
        using var scope = host.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<IAiModelClient>();
        Assert.IsType<GroqAiModelClient>(model);
        Assert.IsAssignableFrom<OpenAiCompatibleModelClient>(model);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    public void UnknownProviderFailsClearlyWhenTheServiceIsResolved(string provider)
    {
        using var host = Host(provider);
        using var scope = host.Services.CreateScope();
        var error = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IAiModelClient>());
        Assert.Contains(AiProviderResolver.ProviderConfiguration, error.Message);
        Assert.Contains(GroqAiModelClient.ProviderName, error.Message);
    }

    [Fact]
    public void TestingRetainsTheDeterministicAdapterWithoutResolvingARealProvider()
    {
        using var host = Host("unknown", AiModule.DeterministicEnvironment);
        using var scope = host.Services.CreateScope();
        Assert.IsType<DeterministicAiModelClient>(scope.ServiceProvider.GetRequiredService<IAiModelClient>());
    }

    private static IHost Host(string? provider, string? environment = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment ?? Environments.Production,
            DisableDefaults = true
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [AiProviderResolver.ProviderConfiguration] = provider
        });
        new AiModule().ConfigureServices(builder.Services);
        return builder.Build();
    }
}
