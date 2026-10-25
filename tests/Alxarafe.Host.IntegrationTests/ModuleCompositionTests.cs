using System.Net.Http.Json;
using Alxarafe.AspNetCore;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Ai;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Http;
using Alxarafe.Modules.Ai.Persistence;
using Alxarafe.Modules.Wms;
using Alxarafe.Modules.Wms.Http;
using Alxarafe.Modules.Wms.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ModuleCompositionTests(TenantApplicationFactory factory) : IClassFixture<TenantApplicationFactory>
{
    [Fact]
    public async Task FreshCompositionKeepsPlatformAndModuleStorageOwnershipSeparate()
    {
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/openapi/v1.json");
        string[] corePaths = ["/health", "/api/auth/login", "/api/auth/register", "/api/auth/me",
            "/api/users", "/api/users/{id}", "/api/modules", "/api/modules/{id}", CapabilityEndpoints.Path, CapabilityEndpoints.Path + "/{capabilityId}", CapabilityEndpoints.Path + "/{capabilityId}/invocations", AiAssistantEndpoints.Path, UomEndpoints.Path, StorageAttributeEndpoints.Path,
            InventoryEndpoints.Warehouses, InventoryEndpoints.Warehouses + "/{id}", InventoryEndpoints.Locations, InventoryEndpoints.Locations + "/{id}",
            InventoryEndpoints.Items, InventoryEndpoints.Items + "/{id}", InventoryEndpoints.Movements, InventoryEndpoints.Movements + "/{id}", InventoryEndpoints.Inventory,
            ItemFamilyEndpoints.Path, ItemFamilyAssociationEndpoints.Path, ItemFamilyAssociationEndpoints.Path + "/{storageAttributeId}"];
        Assert.All(document.GetProperty("paths").EnumerateObject(), path => Assert.Contains(path.Name, corePaths));
        await using var context = await factory.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY table_name COLLATE \"C\"";
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        Assert.Equal([AiUsageStorage.MigrationHistory, PlatformStorage.MigrationHistory, AiUsageStorage.InvocationsTable,
            AiUsageStorage.RecordsTable, PlatformStorage.ModuleStatesTable, TenantAiSettingsStorage.Table,
            TenantStorage.MembershipsTable, TenantStorage.TenantsTable], tables);
        Assert.Equal(4, context.Model.GetEntityTypes().Count());
        Assert.DoesNotContain(context.Model.GetEntityTypes(), type => type.ClrType == typeof(UomRow) || type.ClrType == typeof(StorageAttributeRow)
            || type.ClrType == typeof(ItemFamilyRow) || type.ClrType == typeof(ItemFamilyAssociationRow)
            || type.ClrType == typeof(AiUsageRecord) || type.ClrType == typeof(AiInvocationUsage));
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
        string[] expected = canonical
            ? [AiModule.Id, ConformanceModuleSource.MathId, ConformanceModuleSource.StatisticsId, WmsModule.Id]
            : [AiModule.Id, WmsModule.Id];
        Assert.Equal(expected, registry.Modules.Select(module => module.Descriptor.Id));
        Assert.Equal(ModuleGraph.Order(registry.Modules), runtime.Modules);
        Assert.Same(registry, runtime.Registry);
        Assert.Same(registry.InitializationOrder, runtime.Modules);
        Assert.Equal(expected, registry.Descriptors.Select(descriptor => descriptor.Id));
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
                Assert.False(module is IModuleInitializer);
                Assert.False(module is IModuleServiceContributor);
                Assert.Equal(ModulePersistence.None, module.Descriptor.Persistence);
                Assert.False(module.Descriptor.HasConfiguration);
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
