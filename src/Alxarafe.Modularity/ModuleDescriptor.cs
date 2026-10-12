namespace Alxarafe.Modularity;

public static class ModuleApi
{
    public const int CurrentVersion = 1;
}

public sealed record ModuleDescriptor
{
    public string Id { get; }
    public int ModuleApiVersion { get; }
    public IReadOnlyList<string> Dependencies { get; }

    public ModuleDescriptor(string id, int moduleApiVersion, IEnumerable<string> dependencies)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(dependencies);
        var snapshot = dependencies.ToArray();
        foreach (var dependency in snapshot)
            ArgumentNullException.ThrowIfNull(dependency);
        Id = id;
        ModuleApiVersion = moduleApiVersion;
        Dependencies = Array.AsReadOnly(snapshot);
    }
}
