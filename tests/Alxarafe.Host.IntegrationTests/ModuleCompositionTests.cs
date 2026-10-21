using System.Net.Http.Json;
using Alxarafe.AspNetCore;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms;
using Alxarafe.Modules.Wms.Http;
using Alxarafe.Modules.Wms.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ModuleCompositionTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task FreshCompositionKeepsPlatformAndWmsStorageOwnershipSeparate()
    {
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/openapi/v1.json");
        string[] corePaths = ["/health", "/api/auth/login", "/api/auth/register", "/api/auth/me",
            "/api/users", "/api/users/{id}", "/api/modules", "/api/modules/{id}", CapabilityEndpoints.Path, CapabilityEndpoints.Path + "/{capabilityId}", CapabilityEndpoints.Path + "/{capabilityId}/invocations", UomEndpoints.Path, StorageAttributeEndpoints.Path,
            ItemFamilyEndpoints.Path, ItemFamilyAssociationEndpoints.Path, ItemFamilyAssociationEndpoints.Path + "/{storageAttributeId}"];
        Assert.All(document.GetProperty("paths").EnumerateObject(), path => Assert.Contains(path.Name, corePaths));
        await using var context = await factory.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY table_name COLLATE \"C\"";
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        Assert.Equal([PlatformStorage.MigrationHistory, WmsStorage.MigrationHistory, PlatformStorage.ModuleStatesTable, WmsStorage.ItemFamilyTable, WmsStorage.AssociationTable, WmsStorage.StorageAttributeTable, WmsStorage.UomTable], tables);
        Assert.Single(context.Model.GetEntityTypes());
        Assert.DoesNotContain(context.Model.GetEntityTypes(), type => type.ClrType == typeof(UomRow) || type.ClrType == typeof(StorageAttributeRow)
            || type.ClrType == typeof(ItemFamilyRow) || type.ClrType == typeof(ItemFamilyAssociationRow));
    }

    [Theory]
    [InlineData("Development", true, false)]
    [InlineData("Production", true, false)]
    [InlineData(ConformanceModuleSource.EnvironmentName, false, false)]
    [InlineData(ConformanceModuleSource.EnvironmentName, true, true)]
    public async Task CanonicalModulesRequireExplicitTestingCompositionAlongsideProductionWms(string environment, bool optIn, bool canonical)
    {
        using var application = factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment)
            .UseSetting(ConformanceModuleSource.EnabledConfiguration, optIn.ToString()));
        using var client = application.CreateClient();
        var registry = application.Services.GetRequiredService<ModuleRegistry>();
        var runtime = application.Services.GetRequiredService<ModuleRuntime>();
        string[] expected = canonical ? [ConformanceModuleSource.MathId, ConformanceModuleSource.StatisticsId, WmsModule.Id] : [WmsModule.Id];
        Assert.Equal(expected, registry.Modules.Select(module => module.Descriptor.Id));
        Assert.Equal(ModuleGraph.Order(registry.Modules), runtime.Modules);
        var wms = registry.Modules.Single(module => module.Descriptor.Id == WmsModule.Id);
        Assert.IsType<WmsModule>(wms);
        Assert.Empty(wms.Descriptor.Dependencies);
        Assert.Equal(ModuleApi.CurrentVersion, wms.Descriptor.ModuleApiVersion);
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
