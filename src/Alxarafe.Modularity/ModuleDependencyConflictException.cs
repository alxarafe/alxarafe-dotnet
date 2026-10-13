namespace Alxarafe.Modularity;

public sealed class ModuleDependencyConflictException(string moduleId, string dependencyId)
    : InvalidOperationException($"Enabled module '{moduleId}' requires enabled dependency '{dependencyId}'.");
