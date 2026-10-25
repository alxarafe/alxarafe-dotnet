using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Core;
using Alxarafe.Modules.Ai;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class EffectiveAiConfigurationTests
{
    internal const string GlobalModel = GroqAiModelClient.DefaultModel;
    internal const string LocalModel = OllamaAiModelClient.DefaultModel;
    internal const string ReportedModel = "served-model";
    internal const string SyntheticKey = "synthetic-platform-key";
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string ChangedModel = "changed-model";
    private static readonly AiModelRequest Request = new("Instructions", [new(AiRoles.User, "Hello")],
        [new("authorized_read", "fixture.read", "Read", "Read", JsonSerializer.SerializeToElement(new { type = "object" }))]);

    [Theory]
    [InlineData(null, null, GroqAiModelClient.ProviderName, GlobalModel)]
    [InlineData("", "", GroqAiModelClient.ProviderName, GlobalModel)]
    [InlineData(" ", " ", GroqAiModelClient.ProviderName, GlobalModel)]
    [InlineData(OllamaAiModelClient.ProviderName, LocalModel, OllamaAiModelClient.ProviderName, LocalModel)]
    [InlineData(OllamaAiModelClient.ProviderName, null, OllamaAiModelClient.ProviderName, GlobalModel)]
    [InlineData(null, LocalModel, GroqAiModelClient.ProviderName, LocalModel)]
    public async Task OverridesResolveEachFieldIndependently(string? provider, string? model, string expectedProvider, string expectedModel)
    {
        var store = new Store();
        store.Values[TenantB] = new(TenantB, provider, model);
        var result = await new EffectiveAiConfigurationResolver(Configuration(), new Tenant(TenantB), store).ResolveAsync();
        Assert.Equal(new EffectiveAiConfiguration(expectedProvider, expectedModel), result);
    }

    [Fact]
    public async Task AbsentTenantNeverReadsSettingsAndMissingRowUsesGlobalDefaults()
    {
        var store = new Store();
        var global = await new EffectiveAiConfigurationResolver(Configuration(), new UnavailableTenantContext(), store).ResolveAsync();
        Assert.Equal(new EffectiveAiConfiguration(GroqAiModelClient.ProviderName, GlobalModel), global);
        Assert.Equal(0, store.Reads);
        Assert.Equal(global, await new EffectiveAiConfigurationResolver(Configuration(), new Tenant(TenantA), store).ResolveAsync());
        Assert.Equal(1, store.Reads);
    }

    [Theory]
    [InlineData(GroqAiModelClient.ProviderName, "legacy-model")]
    [InlineData(OllamaAiModelClient.ProviderName, OllamaAiModelClient.DefaultModel)]
    public void GlobalDefaultsKeepTheExistingProviderSpecificLegacyAlias(string provider, string expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [AiProviderResolver.ProviderConfiguration] = provider,
            [GroqAiModelClient.ModelConfiguration] = "legacy-model"
        }).Build();
        Assert.Equal(expected, EffectiveAiConfigurationResolver.Global(configuration).ModelId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownGlobalOrTenantProviderFailsBeforeHttpWithoutFallback(bool tenantOverride)
    {
        var store = new Store();
        store.Values[TenantB] = new(TenantB, tenantOverride ? "unknown" : null);
        var configuration = Configuration(tenantOverride ? GroqAiModelClient.ProviderName : "unknown");
        using var wire = new Wire();
        using var client = new ConfiguredAiModelClient(new HttpClient(wire), configuration,
            new EffectiveAiConfigurationResolver(configuration, new Tenant(TenantB), store), NullLoggerFactory.Instance);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(Request));
        Assert.Contains(AiProviderResolver.ProviderConfiguration, error.Message);
        Assert.Empty(wire.Calls);
    }

    [Theory]
    [InlineData(null, GlobalModel)]
    [InlineData(LocalModel, LocalModel)]
    public async Task ExplicitTenantSelectionDoesNotValidateAnUnusedGlobalProvider(string? modelOverride, string expectedModel)
    {
        var store = new Store();
        store.Values[TenantB] = new(TenantB, OllamaAiModelClient.ProviderName, modelOverride);
        var effective = await new EffectiveAiConfigurationResolver(Configuration("unknown"), new Tenant(TenantB), store).ResolveAsync();
        Assert.Equal(new EffectiveAiConfiguration(OllamaAiModelClient.ProviderName, expectedModel), effective);
    }

    [Fact]
    public async Task ConcurrentScopesFreezeTheirOwnSelectionAndNeverContaminateLaterRequests()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = Environments.Production });
        builder.Configuration.AddConfiguration(Configuration());
        var store = new Store();
        store.Values[TenantB] = new(TenantB, OllamaAiModelClient.ProviderName, LocalModel);
        using var wire = new Wire();
        builder.Services.AddSingleton<ITenantAiSettingsStore>(store);
        builder.Services.AddScoped<ICurrentTenantContext, Tenant>();
        new AiModule().ConfigureServices(builder.Services);
        builder.Services.AddScoped<IAiModelClient>(provider => new ConfiguredAiModelClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(AiModule.HttpClientName), builder.Configuration,
            provider.GetRequiredService<EffectiveAiConfigurationResolver>(), NullLoggerFactory.Instance));
        builder.Services.AddHttpClient(AiModule.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => wire);
        using var host = builder.Build();
        using var a = host.Services.CreateScope();
        using var b = host.Services.CreateScope();
        ((Tenant)a.ServiceProvider.GetRequiredService<ICurrentTenantContext>()).TenantId = TenantA;
        ((Tenant)b.ServiceProvider.GetRequiredService<ICurrentTenantContext>()).TenantId = TenantB;
        var clientA = a.ServiceProvider.GetRequiredService<IAiModelClient>();
        var clientB = b.ServiceProvider.GetRequiredService<IAiModelClient>();
        Assert.NotSame(clientA, clientB);
        Assert.Same(clientA, a.ServiceProvider.GetRequiredService<IAiModelClient>());
        Assert.NotSame(a.ServiceProvider.GetRequiredService<EffectiveAiConfigurationResolver>(), b.ServiceProvider.GetRequiredService<EffectiveAiConfigurationResolver>());
        var results = await Task.WhenAll(clientA.CompleteAsync(Request), clientB.CompleteAsync(Request));
        AssertUsage(results[0], GroqAiModelClient.ProviderName, GlobalModel);
        AssertUsage(results[1], OllamaAiModelClient.ProviderName, LocalModel);
        store.Values[TenantB] = new(TenantB, OllamaAiModelClient.ProviderName, ChangedModel);
        AssertUsage(await clientB.CompleteAsync(Request), OllamaAiModelClient.ProviderName, LocalModel);
        using var next = host.Services.CreateScope();
        ((Tenant)next.ServiceProvider.GetRequiredService<ICurrentTenantContext>()).TenantId = TenantB;
        AssertUsage(await next.ServiceProvider.GetRequiredService<IAiModelClient>().CompleteAsync(Request), OllamaAiModelClient.ProviderName, ChangedModel);
        using var global = host.Services.CreateScope();
        AssertUsage(await global.ServiceProvider.GetRequiredService<IAiModelClient>().CompleteAsync(Request), GroqAiModelClient.ProviderName, GlobalModel);
        Assert.Equal(3, store.Reads);
        foreach (var call in wire.Calls)
        {
            Assert.Equal(call.Model == GlobalModel ? "Bearer " + SyntheticKey : null, call.Authorization);
            Assert.Equal("authorized_read", Assert.Single(call.Tools).GetProperty("function").GetProperty("name").GetString());
        }
        Assert.Equal(GlobalModel, builder.Configuration[AiProviderResolver.ModelConfiguration]);
    }

    private static void AssertUsage(AiModelResponse response, string provider, string model)
    {
        var record = Assert.Single(response.Executions!);
        Assert.Equal(provider, record.Provider);
        Assert.Equal(model, record.Model);
        Assert.Equal(ReportedModel, record.ReportedModel);
        Assert.Null(record.ProviderCost);
        Assert.Null(record.BillingMode);
    }

    internal static IConfiguration Configuration(string provider = GroqAiModelClient.ProviderName) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        [AiProviderResolver.ProviderConfiguration] = provider,
        [AiProviderResolver.ModelConfiguration] = GlobalModel,
        [GroqAiModelClient.ApiKeyConfiguration] = SyntheticKey,
        [GroqAiModelClient.BaseUrlConfiguration] = "https://groq.example.test/v1",
        [OllamaAiModelClient.BaseUrlConfiguration] = "http://ollama.example.test/v1"
    }).Build();

    internal sealed class Tenant(string? tenantId = null) : ICurrentTenantContext
    {
        public string? TenantId { get; set; } = tenantId;
    }

    private sealed class Store : ITenantAiSettingsStore
    {
        private int reads;
        public int Reads => reads;
        public ConcurrentDictionary<string, TenantAiSettings> Values { get; } = new(StringComparer.Ordinal);
        public async Task<TenantAiSettings?> FindAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref reads);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return Values.GetValueOrDefault(tenantId);
        }
    }

    internal sealed class Wire : HttpMessageHandler
    {
        internal sealed record Call(string Model, string? Authorization, JsonElement[] Tools);
        public ConcurrentBag<Call> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            Calls.Add(new(body.GetProperty("model").GetString()!, request.Headers.Authorization?.ToString(),
                body.TryGetProperty("tools", out var tools) ? tools.EnumerateArray().Select(tool => tool.Clone()).ToArray() : []));
            await Task.Yield();
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"response-fixture","model":"served-model","usage":{"prompt_tokens":3,"completion_tokens":2},"choices":[{"message":{"content":"Safe answer."}}]}""")
            };
        }
    }
}
