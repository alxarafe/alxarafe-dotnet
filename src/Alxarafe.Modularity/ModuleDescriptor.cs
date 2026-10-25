namespace Alxarafe.Modularity;

public static class ModuleApi
{
    public const int CurrentVersion = 1;
}

// Describes ownership, not database routing or automatic provisioning.
public enum ModulePersistence { None, Platform, Tenant, Both }

public sealed record ModuleDescriptor
{
    public string Id { get; }
    public string Version { get; }
    public int ModuleApiVersion { get; }
    public IReadOnlyList<string> Dependencies { get; }
    public ModulePersistence Persistence { get; }
    public bool HasConfiguration { get; }

    public ModuleDescriptor(string id, string version, int moduleApiVersion, IEnumerable<string> dependencies,
        ModulePersistence persistence = ModulePersistence.None, bool hasConfiguration = false)
    {
        ValidateId(id, nameof(id));
        if (!System.Version.TryParse(version, out var release) || release.Build < 0 || release.Revision >= 0
            || release.ToString() != version)
            throw new ArgumentException($"Module '{id}' must declare a numeric major.minor.patch version.", nameof(version));
        if (!Enum.IsDefined(persistence))
            throw new ArgumentOutOfRangeException(nameof(persistence), persistence, $"Module '{id}' declares invalid persistence.");
        ArgumentNullException.ThrowIfNull(dependencies);
        var snapshot = dependencies.ToArray();
        foreach (var dependency in snapshot) ValidateId(dependency, nameof(dependencies));
        if (snapshot.Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException($"Module '{id}' declares duplicate dependencies.", nameof(dependencies));
        Id = id;
        Version = version;
        ModuleApiVersion = moduleApiVersion;
        Dependencies = Array.AsReadOnly(snapshot);
        Persistence = persistence;
        HasConfiguration = hasConfiguration;
    }

    private static void ValidateId(string id, string parameter)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
            throw new ArgumentException("Module and dependency IDs must be nonempty without whitespace or control characters.", parameter);
    }
}
