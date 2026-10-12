namespace Alxarafe.Modularity;

/// <summary>A valid available module has been administratively disabled.</summary>
public sealed class ModuleDisabledException(string moduleId) : Exception($"Module '{moduleId}' is disabled.")
{
    public string ModuleId { get; } = moduleId;
}
