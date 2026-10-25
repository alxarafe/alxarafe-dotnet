using Alxarafe.Modules.Wms;
using Alxarafe.Resources;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ResourceStartupTests(TenantApplicationFactory factory) : IClassFixture<TenantApplicationFactory>
{
    private const string ResourceId = "fixture.startup";
    private const string Version = "1.0.0";

    [Fact]
    public void EmptyProductionCatalogAndTestOnlyProviderUseTheSameStartupBoundary()
    {
        using var bootstrap = factory.CreateClient();
        Assert.Empty(factory.Services.GetRequiredService<ResourceRegistry>().All);
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IResourceDefinitionProvider>(new Provider(true))));
        using var client = host.CreateClient();
        var registry = host.Services.GetRequiredService<ResourceRegistry>();
        Assert.Equal(ResourceId, Assert.Single(registry.All).Id);
        Assert.Equal(WmsModule.Id, registry.Require(ResourceId).ModuleId);
    }

    [Fact]
    public void MalformedMetadataPreventsHostStartupRatherThanFailingOnFirstRequest()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IResourceDefinitionProvider>(new Provider(false))));
        var error = Assert.Throws<ResourceValidationException>(() => host.CreateClient());
        Assert.Equal(ResourceValidationCode.InvalidField, error.Code);
        Assert.Equal(ResourceId, error.ResourceId);
    }

    private sealed class Provider(bool valid) : IResourceDefinitionProvider
    {
        public IReadOnlyCollection<ResourceDefinition> GetResourceDefinitions() => [new(ResourceId, Version, WmsModule.Id,
            new("id"), valid ? [new("id", ScalarType.Uuid, readOnly: true, immutable: true)] : [], [], new ResourceSecurity([]))];
    }
}
