using System.Globalization;
using Alxarafe.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class ModularityTests
{
    [Fact]
    public void DescriptorCopiesDependenciesAndExposesSupportedVersion()
    {
        var dependencies = new List<string> { "owner" };
        var descriptor = new ModuleDescriptor("extension", ModuleApi.CurrentVersion, dependencies);
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
        var module = new FakeModule(new ModuleDescriptor("extension", incompatibleVersion, []));
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
        var runtime = new ModuleRuntime(input);
        input.Clear();
        Assert.Same(module, Assert.Single(runtime.Modules));
        Assert.Throws<NotSupportedException>(() => ((IList<IAlxarafeModule>)runtime.Modules).Clear());
    }

    [Fact]
    public async Task BuilderConfiguresAndRuntimeInitializesInResolvedOrder()
    {
        var configured = new List<string>();
        var initialized = new List<string>();
        var owner = new FakeModule(new ModuleDescriptor("z-owner", ModuleApi.CurrentVersion, []), configured, initialized);
        var consumer = new FakeModule(new ModuleDescriptor("a-consumer", ModuleApi.CurrentVersion, [owner.Descriptor.Id]), configured, initialized);
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
        var a = new FakeModule(new ModuleDescriptor("a", ModuleApi.CurrentVersion, []), initialized: calls);
        var b = new FakeModule(new ModuleDescriptor("b", ModuleApi.CurrentVersion, [a.Descriptor.Id]), initialized: calls, failure: failure);
        var c = new FakeModule(new ModuleDescriptor("c", ModuleApi.CurrentVersion, [b.Descriptor.Id]), initialized: calls);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var runtime = new ModuleRuntime(ModuleGraph.Order([c, b, a]));
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

    private static FakeModule Module(string id, params string[] dependencies) =>
        new(new ModuleDescriptor(id, ModuleApi.CurrentVersion, dependencies));

    private sealed record FakeSource(IEnumerable<IAlxarafeModule> Modules) : IModuleSource
    {
        public IEnumerable<IAlxarafeModule> GetModules() => Modules;
    }

    private sealed class FakeModule(ModuleDescriptor descriptor, List<string>? configured = null,
        List<string>? initialized = null, Exception? failure = null) : IAlxarafeModule
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
