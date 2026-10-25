using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Alxarafe.Modularity;

public interface IAlxarafeModule
{
    ModuleDescriptor Descriptor { get; }
}

// Optional composition and technical startup hooks; descriptor-only modules need neither.
public interface IModuleServiceContributor
{
    void ConfigureServices(IServiceCollection services);
}

public interface IModuleInitializer
{
    Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default);
}

public sealed class ModuleRuntime(ModuleRegistry registry)
{
    public ModuleRegistry Registry { get; } = registry;
    public IReadOnlyList<IAlxarafeModule> Modules => Registry.InitializationOrder;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        foreach (var module in Modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (module is IModuleInitializer initializer)
                await initializer.InitializeAsync(services, cancellationToken);
        }
    }
}

public sealed class ModuleBuilder(IServiceCollection services)
{
    private readonly IServiceCollection _services = services;
    private readonly List<IModuleSource> _sources = [];
    private bool _built;

    public ModuleBuilder AddSource(IModuleSource source)
    {
        if (_built) throw new InvalidOperationException("Module composition has already been built.");
        ArgumentNullException.ThrowIfNull(source);
        _sources.Add(source);
        return this;
    }

    public ModuleBuilder AddModulesFromAssemblies(params Assembly[] assemblies) =>
        AddSource(new BuiltInAssemblyModuleSource(assemblies));

    public void Build()
    {
        if (_built || _services.Any(service => service.ServiceType == typeof(ModuleRegistry)))
            throw new InvalidOperationException("Module composition has already been built.");
        _built = true;
        var registry = new ModuleRegistry(_sources);
        // Subsystems validate their own typed contributions before service callbacks.
        var capabilities = new CapabilityRegistry(registry);
        var primitives = new ReadPrimitiveRegistry(registry, capabilities);
        foreach (var module in registry.InitializationOrder)
        {
            if (module is IModuleServiceContributor contributor) contributor.ConfigureServices(_services);
            _services.AddSingleton(module.GetType(), module);
        }
        _services.AddSingleton(registry);
        _services.AddSingleton(capabilities);
        _services.AddSingleton(primitives);
        _services.AddSingleton(new ModuleRuntime(registry));
    }
}

public static class ModuleServiceCollectionExtensions
{
    public static ModuleBuilder AddAlxarafeModules(this IServiceCollection services, params Assembly[] assemblies)
    {
        var builder = new ModuleBuilder(services).AddModulesFromAssemblies(assemblies);
        builder.Build();
        return builder;
    }
}

public static class ModuleGraph
{
    public static IReadOnlyList<IAlxarafeModule> Order(IEnumerable<IAlxarafeModule> availableModules) =>
        Order(availableModules, module => module.Descriptor);

    internal static IReadOnlyList<IAlxarafeModule> Order(IEnumerable<IAlxarafeModule> availableModules,
        Func<IAlxarafeModule, ModuleDescriptor> descriptorFor)
    {
        var modules = new SortedDictionary<string, (IAlxarafeModule Module, ModuleDescriptor Descriptor)>(StringComparer.Ordinal);
        foreach (var module in availableModules)
        {
            var descriptor = descriptorFor(module);
            if (!modules.TryAdd(descriptor.Id, (module, descriptor)))
                throw new InvalidOperationException($"Duplicate module id '{descriptor.Id}'.");
        }
        foreach (var module in modules.Values)
            foreach (var dependency in module.Descriptor.Dependencies)
                if (!modules.ContainsKey(dependency))
                    throw new InvalidOperationException($"Module '{module.Descriptor.Id}' depends on missing module '{dependency}'.");

        var ordered = new List<IAlxarafeModule>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidOperationException($"Module dependency cycle detected at '{id}'.");
            foreach (var dependency in modules[id].Descriptor.Dependencies.Order(StringComparer.Ordinal)) Visit(dependency);
            visiting.Remove(id);
            visited.Add(id);
            ordered.Add(modules[id].Module);
        }
        foreach (var id in modules.Keys) Visit(id);
        return ordered.AsReadOnly();
    }
}
