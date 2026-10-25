using Alxarafe.Modularity;

namespace Alxarafe.Resources.Runtime;

public enum ResourceRepositoryScope { Platform, Tenant }
public sealed record ResourceRepositoryBinding(string ResourceId, ResourceRepositoryScope Scope);

// Startup metadata only; contains no scoped repositories, service providers or factories.
public sealed class ResourceRepositoryCatalog
{
    public IReadOnlyList<ResourceRepositoryBinding> Bindings { get; }
    private readonly Dictionary<string, ResourceRepositoryBinding> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ModulePersistence> _persistence;
    public ResourceRepositoryCatalog(ResourceRegistry resources, ModuleRegistry modules, IEnumerable<ResourceRepositoryBinding> bindings)
    {
        _persistence = modules.Descriptors.ToDictionary(module => module.Id, module => module.Persistence, StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (binding is null || !resources.TryGet(binding.ResourceId, out var resource) || !Enum.IsDefined(binding.Scope)
                || !_bindings.TryAdd(binding.ResourceId, binding)) throw new InvalidOperationException("Invalid or duplicate resource repository binding.");
            var declared = _persistence[resource!.ModuleId];
            if ((declared == ModulePersistence.Tenant && binding.Scope != ResourceRepositoryScope.Tenant)
                || (declared is ModulePersistence.Platform or ModulePersistence.None && binding.Scope != ResourceRepositoryScope.Platform))
                throw new InvalidOperationException($"Repository scope contradicts module persistence for '{binding.ResourceId}'.");
        }
        Bindings = Array.AsReadOnly(_bindings.Values.OrderBy(binding => binding.ResourceId, StringComparer.Ordinal).ToArray());
    }
    public bool Contains(string resourceId) => _bindings.ContainsKey(resourceId);
    internal ResourceRepositoryScope Scope(ResourceDefinition resource) => _persistence[resource.ModuleId] switch
    {
        ModulePersistence.Tenant => ResourceRepositoryScope.Tenant,
        ModulePersistence.Platform or ModulePersistence.None => ResourceRepositoryScope.Platform,
        ModulePersistence.Both when _bindings.TryGetValue(resource.Id, out var binding) => binding.Scope,
        _ => throw new ResourceRuntimeException(resource.Id, ResourceRuntimeError.RepositoryNotRegistered)
    };
}

// Scoped: repository adapters may depend on request-scoped infrastructure. Startup
// resolves one validation scope; each execution scope retains its own immutable map.
public sealed class ResourceRepositoryRegistry
{
    private readonly Dictionary<string, IResourceRepository> _repositories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<IResourceOperationValidator>> _validators = new(StringComparer.Ordinal);
    public ResourceRepositoryRegistry(ResourceRegistry resources, ResourceRepositoryCatalog catalog,
        IEnumerable<IResourceRepository> repositories, IEnumerable<IResourceOperationValidator> validators)
    {
        foreach (var repository in repositories)
            if (repository is null || !catalog.Contains(repository.ResourceId) || !_repositories.TryAdd(repository.ResourceId, repository))
                throw new InvalidOperationException("Unknown, unbound or duplicate resource repository.");
        foreach (var group in validators.GroupBy(validator => validator.ResourceId, StringComparer.Ordinal))
        {
            if (!resources.TryGet(group.Key, out _) || group.Any(validator => string.IsNullOrWhiteSpace(validator.Id))
                || group.Select(validator => validator.Id).Distinct(StringComparer.Ordinal).Count() != group.Count())
                throw new InvalidOperationException("Invalid or duplicate resource business validator.");
            _validators.Add(group.Key, Array.AsReadOnly(group.OrderBy(validator => validator.Id, StringComparer.Ordinal).ToArray()));
        }
    }
    internal IResourceRepository Require(string resourceId) => _repositories.TryGetValue(resourceId, out var repository) && repository.ResourceId == resourceId
        ? repository : throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.RepositoryNotRegistered);
    internal async Task ValidateAsync(ResourceOperationContext context, CancellationToken cancellationToken)
    {
        if (!_validators.TryGetValue(context.Definition.Id, out var validators)) return;
        foreach (var validator in validators)
        {
            if (validator.ResourceId != context.Definition.Id) throw new InvalidOperationException("Resource validator ownership changed.");
            var issues = await validator.ValidateAsync(context, cancellationToken);
            if (issues.Count != 0) throw new ResourceRuntimeException(context.Definition.Id, ResourceRuntimeError.ValidationFailed, issues);
        }
    }
}
