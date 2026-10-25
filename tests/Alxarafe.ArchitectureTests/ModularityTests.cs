using System.Globalization;
using Alxarafe.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class ModularityTests
{
    private const string FixtureVersion = "1.0.0";
    [Fact]
    public void DescriptorCopiesDependenciesAndExposesSupportedVersion()
    {
        var dependencies = new List<string> { "owner" };
        var descriptor = new ModuleDescriptor("extension", FixtureVersion, ModuleApi.CurrentVersion, dependencies);
        dependencies.Clear();
        Assert.Equal("extension", descriptor.Id);
        Assert.Equal(ModuleApi.CurrentVersion, descriptor.ModuleApiVersion);
        Assert.Equal(["owner"], descriptor.Dependencies);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)descriptor.Dependencies).Clear());
    }

    [Fact]
    public void NoSourcesAndEmptySourceAreValid()
    {
        Assert.Empty(new ModuleRegistry([]).Modules);
        var registry = new ModuleRegistry([new FakeSource([])]);
        Assert.Empty(registry.Modules);
        Assert.Empty(ModuleGraph.Order(registry.Modules));
    }

    [Fact]
    public void SingleSourceWithSupportedModuleIsAvailable()
    {
        var module = Module("extension");
        Assert.Same(module, Assert.Single(new ModuleRegistry([new FakeSource([module])]).Modules));
    }

    [Fact]
    public void IncompatibleVersionFailsBeforeServiceRegistration()
    {
        var incompatibleVersion = ModuleApi.CurrentVersion + 1;
        var module = new FakeModule(new ModuleDescriptor("extension", FixtureVersion, incompatibleVersion, []));
        var error = Assert.Throws<InvalidOperationException>(() => new ModuleRegistry([new FakeSource([module])]));
        Assert.Contains(module.Descriptor.Id, error.Message);
        Assert.Contains(incompatibleVersion.ToString(CultureInfo.InvariantCulture), error.Message);
    }

    [Fact]
    public void DuplicateWithinOneSourceFails()
    {
        var module = Module("duplicate");
        var error = Assert.Throws<InvalidOperationException>(() => new ModuleRegistry([new FakeSource([module, module])]));
        Assert.Contains(module.Descriptor.Id, error.Message);
    }

    [Fact]
    public void DuplicateAcrossSourcesFails()
    {
        var first = Module("duplicate");
        var second = Module(first.Descriptor.Id);
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ModuleRegistry([new FakeSource([first]), new FakeSource([second])]));
        Assert.Contains(first.Descriptor.Id, error.Message);
    }

    [Fact]
    public void MultipleSourcesSupportDependenciesAndDeterministicOrdering()
    {
        var owner = Module("z-owner");
        var consumer = Module("a-consumer", owner.Descriptor.Id);
        var first = new FakeSource([consumer]);
        var second = new FakeSource([owner]);
        var registry = new ModuleRegistry([first, second]);
        Assert.Equal([consumer, owner], registry.Modules);
        Assert.Equal(registry.Modules, new ModuleRegistry([second, first]).Modules);
        Assert.Equal([owner, consumer], ModuleGraph.Order(registry.Modules));
    }

    [Fact]
    public void IdentitiesAreOpaqueAndCaseSensitive()
    {
        var upper = Module("Extension");
        var lower = Module("extension");
        Assert.Equal([upper, lower], new ModuleRegistry([new FakeSource([lower, upper])]).Modules);
    }

    [Fact]
    public void AvailableCollectionIsImmutableSnapshot()
    {
        var module = Module("extension");
        var input = new List<IAlxarafeModule> { module };
        var registry = new ModuleRegistry([new FakeSource(input)]);
        input.Clear();
        Assert.Same(module, Assert.Single(registry.Modules));
        Assert.Throws<NotSupportedException>(() => ((IList<IAlxarafeModule>)registry.Modules).Clear());
    }

    [Fact]
    public void GraphRetainsSingleModuleAndNaturallyOrdersIndependentModules()
    {
        var upper = Module("B");
        var lower = Module("a");
        var last = Module("z");
        Assert.Same(upper, Assert.Single(ModuleGraph.Order([upper])));
        Assert.Equal([upper, lower, last], ModuleGraph.Order([last, lower, upper]));
        Assert.Equal([upper, lower, last], ModuleGraph.Order([lower, upper, last]));
    }

    [Fact]
    public void GraphOrdersDependencyChain()
    {
        var a = Module("a");
        var b = Module("b", a.Descriptor.Id);
        var c = Module("c", b.Descriptor.Id);
        Assert.Equal([a, b, c], ModuleGraph.Order([c, b, a]));
    }

    [Fact]
    public void GraphOrdersMultipleDependenciesAndVisitsSharedDependencyOnce()
    {
        var root = Module("root");
        var b = Module("b", root.Descriptor.Id);
        var c = Module("c", root.Descriptor.Id);
        var a = Module("a", c.Descriptor.Id, b.Descriptor.Id);
        Assert.Equal([root, b, c, a], ModuleGraph.Order([a, c, root, b]));
        var reversed = Module(a.Descriptor.Id, b.Descriptor.Id, c.Descriptor.Id);
        Assert.Equal([root, b, c, reversed], ModuleGraph.Order([b, root, c, reversed]));
    }

    [Fact]
    public void GraphRejectsMissingDependencyWithBothIds()
    {
        var module = Module("consumer", "missing");
        var error = Assert.Throws<InvalidOperationException>(() => ModuleGraph.Order([module]));
        Assert.Contains(module.Descriptor.Id, error.Message);
        Assert.Contains(module.Descriptor.Dependencies[0], error.Message);
    }

    [Fact]
    public void GraphRejectsDuplicates()
    {
        var module = Module("duplicate");
        Assert.Contains(module.Descriptor.Id,
            Assert.Throws<InvalidOperationException>(() => ModuleGraph.Order([module, module])).Message);
    }

    [Fact]
    public void GraphRejectsDirectCycle()
    {
        var module = Module("self", "self");
        var error = Assert.Throws<InvalidOperationException>(() => ModuleGraph.Order([module]));
        Assert.Contains("cycle", error.Message);
        Assert.Contains(module.Descriptor.Id, error.Message);
    }

    [Fact]
    public void GraphRejectsIndirectCycle()
    {
        var a = Module("a", "b");
        var b = Module("b", "c");
        var c = Module("c", a.Descriptor.Id);
        var error = Assert.Throws<InvalidOperationException>(() => ModuleGraph.Order([c, b, a]));
        Assert.Contains("cycle", error.Message);
        Assert.Contains(a.Descriptor.Id, error.Message);
    }

    [Fact]
    public void GraphAndRuntimeCollectionsAreReadOnlySnapshots()
    {
        var module = Module("a");
        var ordered = ModuleGraph.Order([module]);
        Assert.Throws<NotSupportedException>(() => ((IList<IAlxarafeModule>)ordered).Clear());
        var input = new List<IAlxarafeModule> { module };
        var runtime = new ModuleRuntime(new ModuleRegistry([new FakeSource(input)]));
        input.Clear();
        Assert.Same(module, Assert.Single(runtime.Modules));
        Assert.Throws<NotSupportedException>(() => ((IList<IAlxarafeModule>)runtime.Modules).Clear());
    }

    [Fact]
    public async Task BuilderConfiguresAndRuntimeInitializesInResolvedOrder()
    {
        var configured = new List<string>();
        var initialized = new List<string>();
        var owner = new FakeModule(new ModuleDescriptor("z-owner", FixtureVersion, ModuleApi.CurrentVersion, []), configured, initialized);
        var consumer = new FakeModule(new ModuleDescriptor("a-consumer", FixtureVersion, ModuleApi.CurrentVersion, [owner.Descriptor.Id]), configured, initialized);
        var services = new ServiceCollection();
        new ModuleBuilder(services).AddSource(new FakeSource([consumer])).AddSource(new FakeSource([owner])).Build();
        using var provider = services.BuildServiceProvider();
        Assert.Equal([owner.Descriptor.Id, consumer.Descriptor.Id], configured);
        var runtime = provider.GetRequiredService<ModuleRuntime>();
        Assert.Equal([owner, consumer], runtime.Modules);
        Assert.Equal([consumer, owner], provider.GetRequiredService<ModuleRegistry>().Modules);
        await runtime.InitializeAsync(provider);
        Assert.Equal([owner.Descriptor.Id, consumer.Descriptor.Id], initialized);
    }

    [Fact]
    public async Task RuntimePropagatesFailureAndStopsLaterModules()
    {
        var calls = new List<string>();
        var failure = new InvalidOperationException("fixture failure");
        var a = new FakeModule(new ModuleDescriptor("a", FixtureVersion, ModuleApi.CurrentVersion, []), initialized: calls);
        var b = new FakeModule(new ModuleDescriptor("b", FixtureVersion, ModuleApi.CurrentVersion, [a.Descriptor.Id]), initialized: calls, failure: failure);
        var c = new FakeModule(new ModuleDescriptor("c", FixtureVersion, ModuleApi.CurrentVersion, [b.Descriptor.Id]), initialized: calls);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var runtime = new ModuleRuntime(new ModuleRegistry([new FakeSource([c, b, a])]));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.InitializeAsync(provider)));
        Assert.Equal([a.Descriptor.Id, b.Descriptor.Id], calls);
    }

    [Fact]
    public async Task EmptyRuntimeInitializesNormally()
    {
        var services = new ServiceCollection();
        new ModuleBuilder(services).AddSource(new FakeSource([])).Build();
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<ModuleRuntime>();
        await runtime.InitializeAsync(provider);
        Assert.Empty(runtime.Modules);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" leading")]
    [InlineData("embedded space")]
    [InlineData("control\n")]
    public void MalformedModuleAndDependencyIdsFailExplicitly(string? id)
    {
        Assert.Throws<ArgumentException>(() => new ModuleDescriptor(id!, FixtureVersion, ModuleApi.CurrentVersion, []));
        Assert.Throws<ArgumentException>(() => new ModuleDescriptor("valid", FixtureVersion, ModuleApi.CurrentVersion, [id!]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0.0")]
    [InlineData("invalid")]
    public void MalformedModuleReleaseVersionFailsExplicitly(string? version) =>
        Assert.Throws<ArgumentException>(() => new ModuleDescriptor("fixture", version!, ModuleApi.CurrentVersion, []));

    [Fact]
    public void InvalidPersistenceAndDuplicateDependenciesFailExplicitly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModuleDescriptor("fixture", FixtureVersion,
            ModuleApi.CurrentVersion, [], (ModulePersistence)int.MaxValue));
        Assert.Throws<ArgumentException>(() => new ModuleDescriptor("fixture", FixtureVersion,
            ModuleApi.CurrentVersion, ["owner", "owner"]));
    }

    [Theory]
    [InlineData(ModulePersistence.None)]
    [InlineData(ModulePersistence.Platform)]
    [InlineData(ModulePersistence.Tenant)]
    [InlineData(ModulePersistence.Both)]
    public void DescriptorDeclaresPersistenceAndModuleOwnedConfigurationWithoutRouting(ModulePersistence persistence)
    {
        var descriptor = new ModuleDescriptor("fixture", FixtureVersion, ModuleApi.CurrentVersion, [], persistence, hasConfiguration: true);
        Assert.Equal(FixtureVersion, descriptor.Version);
        Assert.Equal(persistence, descriptor.Persistence);
        Assert.True(descriptor.HasConfiguration);
    }

    [Fact]
    public async Task DescriptorOnlyModulesNeedNoEmptyCompositionOrInitializationHooks()
    {
        var first = new DescriptorOnlyModule(new ModuleDescriptor("a", FixtureVersion, ModuleApi.CurrentVersion, []));
        var second = new DescriptorOnlyModule(new ModuleDescriptor("b", FixtureVersion, ModuleApi.CurrentVersion, [first.Descriptor.Id]));
        var services = new ServiceCollection();
        new ModuleBuilder(services).AddSource(new FakeSource([second, first])).Build();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ModuleRegistry>();
        Assert.Equal([first.Descriptor, second.Descriptor], registry.Descriptors);
        Assert.Equal([first, second], registry.InitializationOrder);
        Assert.All(registry.Descriptors, descriptor =>
        {
            Assert.Equal(ModulePersistence.None, descriptor.Persistence);
            Assert.False(descriptor.HasConfiguration);
        });
        var runtime = provider.GetRequiredService<ModuleRuntime>();
        Assert.Same(registry, runtime.Registry);
        Assert.Same(registry.InitializationOrder, runtime.Modules);
        await runtime.InitializeAsync(provider);
        Assert.Throws<NotSupportedException>(() => ((IList<ModuleDescriptor>)registry.Descriptors).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<IAlxarafeModule>)registry.InitializationOrder).Clear());
    }

    [Fact]
    public async Task RegistryOwnsThreeStageDependencyOrderAndOptionalInitializer()
    {
        var calls = new List<string>();
        var a = new InitializerOnlyModule(new ModuleDescriptor("a", FixtureVersion, ModuleApi.CurrentVersion, []), calls);
        var b = new InitializerOnlyModule(new ModuleDescriptor("b", FixtureVersion, ModuleApi.CurrentVersion, [a.Descriptor.Id]), calls);
        var c = new InitializerOnlyModule(new ModuleDescriptor("c", FixtureVersion, ModuleApi.CurrentVersion, [b.Descriptor.Id]), calls);
        var services = new ServiceCollection();
        new ModuleBuilder(services).AddSource(new FakeSource([c, a, b])).Build();
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ModuleRuntime>().InitializeAsync(provider);
        Assert.Equal([a.Descriptor.Id, b.Descriptor.Id, c.Descriptor.Id], calls);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("cycle")]
    public void InvalidGraphFailsBeforeAnyServiceContribution(string fault)
    {
        var configured = new List<string>();
        var first = new FakeModule(new ModuleDescriptor("a", FixtureVersion, ModuleApi.CurrentVersion,
            fault == "cycle" ? ["b"] : []), configured);
        var second = new FakeModule(new ModuleDescriptor(fault == "duplicate" ? "a" : "b", FixtureVersion,
            ModuleApi.CurrentVersion, fault == "missing" ? ["missing"] : ["a"]), configured);
        var builder = new ModuleBuilder(new ServiceCollection()).AddSource(new FakeSource([second, first]));
        var error = Assert.Throws<InvalidOperationException>(builder.Build);
        Assert.Contains(fault, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(configured);
    }

    [Fact]
    public void BuilderRejectsRepeatedBuildLateSourcesAndASecondCatalog()
    {
        var services = new ServiceCollection();
        var builder = new ModuleBuilder(services);
        builder.Build();
        Assert.Throws<InvalidOperationException>(builder.Build);
        Assert.Throws<InvalidOperationException>(() => builder.AddSource(new FakeSource([])));
        Assert.Throws<InvalidOperationException>(() => new ModuleBuilder(services).Build());
        Assert.Single(services, service => service.ServiceType == typeof(ModuleRegistry));
    }

    [Fact]
    public void CapturedIdentityCannotBeRedefinedToClaimAnotherModulesContribution()
    {
        var a = new ChangingModule(new ModuleDescriptor("a", FixtureVersion, ModuleApi.CurrentVersion, []));
        var b = Module("b");
        var registry = new ModuleRegistry([new FakeSource([b, a])]);
        var original = registry.GetDescriptor(a);
        a.Descriptor = b.Descriptor;
        Assert.Equal("a", registry.GetDescriptor(a).Id);
        Assert.Same(original, registry.Descriptors[0]);
        Assert.Equal([a, b], registry.InitializationOrder);
        Assert.Throws<InvalidOperationException>(() => new CapabilityRegistry(registry));
        Assert.Throws<InvalidOperationException>(() => registry.GetDescriptor(Module("unregistered")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullModuleOrDescriptorFailsExplicitlyDuringCatalogConstruction(bool nullModule)
    {
        IAlxarafeModule module = nullModule ? null! : new DescriptorOnlyModule(null!);
        var error = Assert.Throws<InvalidOperationException>(() => new ModuleRegistry([new FakeSource([module])]));
        Assert.Contains("null module or descriptor", error.Message);
    }

    private sealed record DescriptorOnlyModule(ModuleDescriptor Descriptor) : IAlxarafeModule;

    private sealed class InitializerOnlyModule(ModuleDescriptor descriptor, List<string> calls) : IAlxarafeModule, IModuleInitializer
    {
        public ModuleDescriptor Descriptor { get; } = descriptor;
        public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            calls.Add(Descriptor.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class ChangingModule(ModuleDescriptor descriptor) : IAlxarafeModule, ICapabilityProvider
    {
        private const string Schema = "#/components/schemas/Fixture";
        public ModuleDescriptor Descriptor { get; set; } = descriptor;
        public IEnumerable<string> GetSchemaReferences() => [Schema];
        public IEnumerable<CapabilityDescriptor> GetCapabilities() =>
            [new("fixture.items.list", Descriptor.Id, FixtureVersion, "Fixture", "Fixture read", "read", new(Schema), new(Schema), [])];
    }

    private static FakeModule Module(string id, params string[] dependencies) =>
        new(new ModuleDescriptor(id, FixtureVersion, ModuleApi.CurrentVersion, dependencies));

    private sealed record FakeSource(IEnumerable<IAlxarafeModule> Modules) : IModuleSource
    {
        public IEnumerable<IAlxarafeModule> GetModules() => Modules;
    }

    private sealed class FakeModule(ModuleDescriptor descriptor, List<string>? configured = null,
        List<string>? initialized = null, Exception? failure = null) : IAlxarafeModule, IModuleServiceContributor, IModuleInitializer
    {
        public ModuleDescriptor Descriptor { get; } = descriptor;
        public void ConfigureServices(IServiceCollection services) => configured?.Add(Descriptor.Id);
        public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            initialized?.Add(Descriptor.Id);
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }
}
