namespace Alxarafe.Modularity;

// Presence is the authority for AVAILABLE. Identity is captured once, never re-read
// from module implementations by graph, state, HTTP or contribution consumers.
public sealed class ModuleRegistry
{
    private readonly Dictionary<IAlxarafeModule, ModuleDescriptor> _descriptors = new(ReferenceEqualityComparer.Instance);
    public IReadOnlyList<IAlxarafeModule> Modules { get; }
    public IReadOnlyList<ModuleDescriptor> Descriptors { get; }
    public IReadOnlyList<IAlxarafeModule> InitializationOrder { get; }

    public ModuleRegistry(IEnumerable<IModuleSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var modules = new SortedDictionary<string, IAlxarafeModule>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            foreach (var module in source.GetModules())
            {
                if (module is null || module.Descriptor is not { } descriptor)
                    throw new InvalidOperationException("Module source returned a null module or descriptor.");
                if (descriptor.ModuleApiVersion != ModuleApi.CurrentVersion)
                    throw new InvalidOperationException($"Module '{descriptor.Id}' declares unsupported Module API version {descriptor.ModuleApiVersion}; supported version is {ModuleApi.CurrentVersion}.");
                if (!modules.TryAdd(descriptor.Id, module))
                    throw new InvalidOperationException($"Duplicate module id '{descriptor.Id}'.");
                if (!_descriptors.TryAdd(module, descriptor))
                    throw new InvalidOperationException("A module instance cannot register multiple identities.");
            }
        }
        Modules = Array.AsReadOnly(modules.Values.ToArray());
        Descriptors = Array.AsReadOnly(Modules.Select(GetDescriptor).ToArray());
        InitializationOrder = ModuleGraph.Order(Modules, GetDescriptor);
    }

    public ModuleDescriptor GetDescriptor(IAlxarafeModule module) => _descriptors.TryGetValue(module, out var descriptor)
        ? descriptor : throw new InvalidOperationException("Module is not registered in this catalog.");
}
