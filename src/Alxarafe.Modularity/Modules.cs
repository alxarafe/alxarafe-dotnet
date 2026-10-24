using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Alxarafe.Modularity;

public interface IAlxarafeModule
{
    ModuleDescriptor Descriptor { get; }
    void ConfigureServices(IServiceCollection services);
    Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default);
}

public sealed class ModuleRuntime(IReadOnlyList<IAlxarafeModule> modules)
{
    public IReadOnlyList<IAlxarafeModule> Modules { get; } = Array.AsReadOnly(modules.ToArray());

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        foreach (var module in Modules)
            await module.InitializeAsync(services, cancellationToken);
    }
}

public sealed class ModuleBuilder(IServiceCollection services)
{
    private readonly IServiceCollection _services = services;
    private readonly List<IModuleSource> _sources = [];

    public ModuleBuilder AddSource(IModuleSource source)
    {
        _sources.Add(source);
        return this;
    }

    public ModuleBuilder AddModulesFromAssemblies(params Assembly[] assemblies) =>
        AddSource(new BuiltInAssemblyModuleSource(assemblies));

    public void Build()
    {
        var registry = new ModuleRegistry(_sources);
        var modules = ModuleGraph.Order(registry.Modules);
        foreach (var module in modules)
        {
            module.ConfigureServices(_services);
            _services.AddSingleton(module.GetType(), module);
        }
        _services.AddSingleton(registry);
        var capabilities = new CapabilityRegistry(registry);
        _services.AddSingleton(capabilities);
        _services.AddSingleton(new ReadPrimitiveRegistry(registry, capabilities));
        _services.AddSingleton(new ModuleRuntime(modules));
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
    public static IReadOnlyList<IAlxarafeModule> Order(IEnumerable<IAlxarafeModule> availableModules)
    {
        var modules = new SortedDictionary<string, IAlxarafeModule>(StringComparer.Ordinal);
        foreach (var module in availableModules)
            if (!modules.TryAdd(module.Descriptor.Id, module))
                throw new InvalidOperationException($"Duplicate module id '{module.Descriptor.Id}'.");
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
            ordered.Add(modules[id]);
        }
        foreach (var id in modules.Keys) Visit(id);
        return ordered.AsReadOnly();
    }
}
