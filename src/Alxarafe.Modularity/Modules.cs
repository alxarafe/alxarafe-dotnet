using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Alxarafe.Modularity;

public interface IAlxarafeModule
{
    string Id { get; }
    IReadOnlyCollection<string> Dependencies { get; }
    void ConfigureServices(IServiceCollection services);
    Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default);
}

public sealed class ModuleRuntime(IReadOnlyList<IAlxarafeModule> modules)
{
    public IReadOnlyList<IAlxarafeModule> Modules { get; } = modules;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        foreach (var module in Modules)
            await module.InitializeAsync(services, cancellationToken);
    }
}

public sealed class ModuleBuilder(IServiceCollection services)
{
    private readonly IServiceCollection _services = services;
    private readonly List<Assembly> _assemblies = [];

    public ModuleBuilder AddModulesFromAssemblies(params Assembly[] assemblies)
    {
        _assemblies.AddRange(assemblies);
        return this;
    }

    public void Build()
    {
        var modules = ModuleGraph.Discover(_assemblies);
        foreach (var module in modules)
        {
            module.ConfigureServices(_services);
            _services.AddSingleton(module.GetType(), module);
        }
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
    public static IReadOnlyList<IAlxarafeModule> Discover(IEnumerable<Assembly> assemblies)
    {
        var types = assemblies.SelectMany(a => a.GetExportedTypes())
            .Where(t => typeof(IAlxarafeModule).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .Distinct()
            .ToArray();
        var modules = types.Select(t => (IAlxarafeModule)Activator.CreateInstance(t)!).ToDictionary(m => m.Id, StringComparer.Ordinal);
        foreach (var module in modules.Values)
            foreach (var dependency in module.Dependencies)
                if (!modules.ContainsKey(dependency))
                    throw new InvalidOperationException($"Module '{module.Id}' depends on missing module '{dependency}'.");

        var ordered = new List<IAlxarafeModule>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidOperationException($"Module dependency cycle detected at '{id}'.");
            foreach (var dependency in modules[id].Dependencies) Visit(dependency);
            visiting.Remove(id);
            visited.Add(id);
            ordered.Add(modules[id]);
        }
        foreach (var id in modules.Keys.Order(StringComparer.Ordinal)) Visit(id);
        return ordered;
    }
}
