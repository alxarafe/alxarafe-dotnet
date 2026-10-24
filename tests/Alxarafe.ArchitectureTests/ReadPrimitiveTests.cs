using System.Text.Json;
using Alxarafe.Modularity;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Http;
using Alxarafe.Modules.Ai.Infrastructure;
using Alxarafe.Modules.Wms;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class ReadPrimitiveTests
{
    [Fact]
    public void AiAdaptersHaveNoWmsOrPersistenceReferencesAndWmsOwnsFiveExplicitReadOperations()
    {
        foreach (var assembly in new[] { typeof(AiAssistant).Assembly, typeof(GroqAiModelClient).Assembly, typeof(AiAssistantEndpoints).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.Contains("Wms", StringComparison.Ordinal)
                || reference.Name.Contains("EntityFramework", StringComparison.Ordinal));
        Assert.IsAssignableFrom<IReadPrimitiveProvider>(new WmsModule());
        Assert.Equal(5, WmsReadPrimitives.All.Count);
        Assert.Equal(5, WmsReadPrimitives.All.Select(primitive => primitive.Id).Distinct().Count());
        Assert.Equal(5, WmsReadPrimitives.All.Select(primitive => primitive.Name).Distinct().Count());
        foreach (var primitive in WmsReadPrimitives.All)
        {
            Assert.Equal(WmsModule.Id, primitive.Module);
            Assert.False(primitive.InputSchema.GetProperty("additionalProperties").GetBoolean());
            Assert.DoesNotContain("/api/", primitive.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("DbContext", primitive.Description, StringComparison.Ordinal);
            var fields = primitive.InputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray();
            Assert.DoesNotContain("url", fields);
            Assert.DoesNotContain("method", fields);
            Assert.DoesNotContain("path", fields);
            Assert.Equal(100, primitive.InputSchema.GetProperty("properties").GetProperty("limit").GetProperty("maximum").GetInt32());
        }
    }

    [Fact]
    public void RegistryRejectsDuplicateIdentitiesNamesAndOpenSchemas()
    {
        var valid = WmsReadPrimitives.All[0];
        Assert.Throws<InvalidOperationException>(() => Registry([valid, valid]));
        Assert.Throws<InvalidOperationException>(() => Registry([valid, valid with { Id = "wms.other.list" }]));
        Assert.Throws<InvalidOperationException>(() => Registry([valid with { InputSchema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { } }) }]));
        Assert.Throws<InvalidOperationException>(() => Registry([valid with { Module = "erbas.other" }]));
        Assert.Single(Registry([valid]).Primitives);
    }
    private static ReadPrimitiveRegistry Registry(ReadPrimitiveDescriptor[] descriptors)
    {
        var modules = new ModuleRegistry([new Source(new Provider(descriptors))]);
        return new(modules, new CapabilityRegistry(modules));
    }
    private sealed class Source(IAlxarafeModule module) : IModuleSource
    {
        public IEnumerable<IAlxarafeModule> GetModules() => [module];
    }
    private sealed class Provider(ReadPrimitiveDescriptor[] descriptors) : IAlxarafeModule, IReadPrimitiveProvider
    {
        public ModuleDescriptor Descriptor { get; } = new(WmsModule.Id, ModuleApi.CurrentVersion, []);
        public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services) { }
        public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IEnumerable<ReadPrimitiveDescriptor> GetReadPrimitives() => descriptors;
    }
}
