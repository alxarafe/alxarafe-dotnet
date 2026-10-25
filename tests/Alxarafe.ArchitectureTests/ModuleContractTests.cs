using Alxarafe.AspNetCore;
using Alxarafe.Core;
using Alxarafe.Modularity;
using Alxarafe.Modules.Ai;
using Alxarafe.Modules.Wms;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class ModuleContractTests
{
    private const string ModuleAssemblyPrefix = "Alxarafe.Modules.";

    [Fact]
    public void ProductionModulesDeclareTheirOwnPersistenceAndConfiguration()
    {
        var wms = new WmsModule();
        var ai = new AiModule();
        Assert.Equal(ModulePersistence.Tenant, wms.Descriptor.Persistence);
        Assert.Equal(ModulePersistence.Platform, ai.Descriptor.Persistence);
        Assert.Equal(WmsModule.Version, wms.Descriptor.Version);
        Assert.Equal(AiModule.Version, ai.Descriptor.Version);
        foreach (var module in new IAlxarafeModule[] { wms, ai })
        {
            Assert.True(module.Descriptor.HasConfiguration);
            Assert.Empty(module.Descriptor.Dependencies);
            Assert.IsAssignableFrom<IModuleServiceContributor>(module);
            Assert.IsAssignableFrom<IModuleInitializer>(module);
        }
        var registry = new ModuleRegistry([new Source([wms, ai])]);
        Assert.Equal(new[] { AiModule.Id, WmsModule.Id }, registry.Descriptors.Select(descriptor => descriptor.Id));
    }

    [Fact]
    public void FrameworkAssembliesNeverReferenceBusinessModules()
    {
        foreach (var assembly in new[] { typeof(Tenant).Assembly, typeof(ModuleDescriptor).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
                reference.Name!.StartsWith(ModuleAssemblyPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void TypedContributionsAreOwnedByTheirSubsystemsAndDoNotExpandTheBaseContract()
    {
        var core = typeof(Tenant).Assembly;
        Assert.NotEqual(core, typeof(ICapabilityProvider).Assembly);
        Assert.NotEqual(core, typeof(IReadPrimitiveProvider).Assembly);
        Assert.NotEqual(core, typeof(IHttpModule).Assembly);
        Assert.NotEqual(typeof(ModuleDescriptor).Assembly, typeof(IHttpModule).Assembly);
        Assert.Equal([nameof(IAlxarafeModule.Descriptor)], typeof(IAlxarafeModule).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(typeof(IAlxarafeModule).GetMethods(), method => !method.IsSpecialName);
        Assert.All(typeof(ModuleDescriptor).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(typeof(ModuleDescriptor).GetProperties(), property =>
            property.PropertyType == typeof(IServiceProvider) || property.PropertyType == typeof(Type)
            || property.PropertyType == typeof(ICapabilityProvider) || property.PropertyType == typeof(IReadPrimitiveProvider));
    }

    private sealed record Source(IAlxarafeModule[] Modules) : IModuleSource
    {
        public IEnumerable<IAlxarafeModule> GetModules() => Modules;
    }
}
