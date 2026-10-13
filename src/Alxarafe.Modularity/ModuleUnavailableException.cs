namespace Alxarafe.Modularity;

public sealed class ModuleUnavailableException(string moduleId)
    : InvalidOperationException($"Module '{moduleId}' is not AVAILABLE.");
