using System.Reflection;

namespace Alxarafe.Modularity;

public interface IModuleSource
{
    IEnumerable<IAlxarafeModule> GetModules();
}

// Existing built-in composition only; this source does not load external assemblies.
public sealed class BuiltInAssemblyModuleSource(IEnumerable<Assembly> assemblies) : IModuleSource
{
    private readonly Assembly[] _assemblies = assemblies.Distinct().ToArray();

    public IEnumerable<IAlxarafeModule> GetModules() => _assemblies
        .SelectMany(assembly => assembly.GetExportedTypes())
        .Where(type => typeof(IAlxarafeModule).IsAssignableFrom(type)
            && type is { IsAbstract: false, IsInterface: false })
        .Distinct()
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .Select(type => (IAlxarafeModule)Activator.CreateInstance(type)!);
}
