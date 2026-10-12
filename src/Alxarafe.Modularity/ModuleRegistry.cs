namespace Alxarafe.Modularity;

// Presence in the registry is the authority for AVAILABLE in this startup.
public sealed class ModuleRegistry
{
    public IReadOnlyList<IAlxarafeModule> Modules { get; }

    public ModuleRegistry(IEnumerable<IModuleSource> sources)
    {
        var modules = new SortedDictionary<string, IAlxarafeModule>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            foreach (var module in source.GetModules())
            {
                var descriptor = module.Descriptor;
                if (descriptor.ModuleApiVersion != ModuleApi.CurrentVersion)
                    throw new InvalidOperationException($"Module '{descriptor.Id}' declares unsupported Module API version {descriptor.ModuleApiVersion}; supported version is {ModuleApi.CurrentVersion}.");
                if (!modules.TryAdd(descriptor.Id, module))
                    throw new InvalidOperationException($"Duplicate module id '{descriptor.Id}'.");
            }
        }
        Modules = Array.AsReadOnly(modules.Values.ToArray());
    }
}
