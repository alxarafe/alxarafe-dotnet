using Alxarafe.AspNetCore;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.AiAgent.ModuleDefinition;
using Alxarafe.Modules.Catalog.ModuleDefinition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ModuleCompositionTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("Development", true, false)]
    [InlineData("Production", true, false)]
    [InlineData(ConformanceModuleSource.EnvironmentName, false, false)]
    [InlineData(ConformanceModuleSource.EnvironmentName, true, true)]
    public async Task CanonicalModulesRequireExplicitTestingCompositionAndPreserveBusinessModules(string environment, bool optIn, bool canonical)
    {
        using var application = factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment)
            .UseSetting(ConformanceModuleSource.EnabledConfiguration, optIn.ToString()));
        using var client = application.CreateClient();
        var registry = application.Services.GetRequiredService<ModuleRegistry>();
        var runtime = application.Services.GetRequiredService<ModuleRuntime>();
        string[] business = [new AiAgentModule().Descriptor.Id, new CatalogModule().Descriptor.Id];
        string[] expected = canonical ? [.. business, ConformanceModuleSource.MathId, ConformanceModuleSource.StatisticsId] : business;
        Assert.Equal(expected, registry.Modules.Select(module => module.Descriptor.Id));
        Assert.Equal(ModuleGraph.Order(registry.Modules), runtime.Modules);
        Assert.Single(registry.Modules.OfType<AiAgentModule>());
        Assert.Single(registry.Modules.OfType<CatalogModule>());
        if (!canonical) return;
        try
        {
            var math = registry.Modules.Single(module => module.Descriptor.Id == ConformanceModuleSource.MathId);
            var statistics = registry.Modules.Single(module => module.Descriptor.Id == ConformanceModuleSource.StatisticsId);
            Assert.Empty(math.Descriptor.Dependencies);
            Assert.Equal([ConformanceModuleSource.MathId], statistics.Descriptor.Dependencies);
            foreach (var module in new[] { math, statistics })
            {
                Assert.Equal(ModuleApi.CurrentVersion, module.Descriptor.ModuleApiVersion);
                Assert.False(module is IHttpModule);
                Assert.Equal(ModuleStateService.InitialEnabled, await application.Services.GetRequiredService<IModuleStateStore>().FindAsync(module.Descriptor.Id));
            }
        }
        finally
        {
            await using var context = await application.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
            await context.ModuleStates.Where(row => row.ModuleId == ConformanceModuleSource.MathId || row.ModuleId == ConformanceModuleSource.StatisticsId).ExecuteDeleteAsync();
        }
    }
}
