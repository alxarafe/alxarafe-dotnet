namespace Alxarafe.Modularity;

public interface IModuleStateStore
{
    Task<bool?> FindAsync(string moduleId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Action<IDictionary<string, bool>> change, CancellationToken cancellationToken = default);
}

public sealed class ModuleStateService
{
    public const bool InitialEnabled = true;
    private readonly SortedDictionary<string, ModuleDescriptor> _available = new(StringComparer.Ordinal);
    private readonly IModuleStateStore _store;

    public ModuleStateService(ModuleRegistry registry, IModuleStateStore store)
    {
        foreach (var module in registry.Modules) _available.Add(module.Descriptor.Id, module.Descriptor);
        _store = store;
    }

    public Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        if (_available.Count == 0) return Task.CompletedTask;
        return _store.UpdateAsync(states =>
        {
            foreach (var id in _available.Keys) states.TryAdd(id, InitialEnabled);
            Validate(states);
        }, cancellationToken);
    }

    public async Task<bool> IsEnabledAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        RequireAvailable(moduleId);
        return await _store.FindAsync(moduleId, cancellationToken)
            ?? throw new InvalidOperationException($"Module state is missing for '{moduleId}'.");
    }

    public async Task RequireEnabledAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync(moduleId, cancellationToken))
            throw new InvalidOperationException($"Module '{moduleId}' is disabled.");
    }

    public Task EnableAsync(string moduleId, CancellationToken cancellationToken = default) => ChangeAsync(moduleId, true, cancellationToken);
    public Task DisableAsync(string moduleId, CancellationToken cancellationToken = default) => ChangeAsync(moduleId, false, cancellationToken);

    private Task ChangeAsync(string moduleId, bool enabled, CancellationToken cancellationToken)
    {
        RequireAvailable(moduleId);
        return _store.UpdateAsync(states =>
        {
            if (!states.ContainsKey(moduleId)) throw new InvalidOperationException($"Module state is missing for '{moduleId}'.");
            states[moduleId] = enabled;
            Validate(states);
        }, cancellationToken);
    }

    private void RequireAvailable(string moduleId)
    {
        if (!_available.ContainsKey(moduleId)) throw new InvalidOperationException($"Module '{moduleId}' is not AVAILABLE.");
    }

    private void Validate(IDictionary<string, bool> states)
    {
        foreach (var module in _available.Values)
        {
            if (!states.TryGetValue(module.Id, out var enabled))
                throw new InvalidOperationException($"Module state is missing for '{module.Id}'.");
            if (!enabled) continue;
            foreach (var dependency in module.Dependencies)
                if (!states.TryGetValue(dependency, out var dependencyEnabled) || !dependencyEnabled)
                    throw new InvalidOperationException($"Enabled module '{module.Id}' requires enabled dependency '{dependency}'.");
        }
    }
}
