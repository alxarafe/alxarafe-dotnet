using System.Collections.ObjectModel;
using Alxarafe.Modularity;

namespace Alxarafe.Resources;

public interface IResourceDefinitionProvider
{
    IReadOnlyCollection<ResourceDefinition> GetResourceDefinitions();
}

// Construct eagerly at the composition/initialization boundary, before serving requests.
// No mutable registration API and no provider callbacks after construction.
public sealed class ResourceRegistry
{
    private readonly ReadOnlyDictionary<string, ResourceDefinition> _resources;
    public IReadOnlyList<ResourceDefinition> All { get; }
    public IReadOnlyDictionary<string, string> DefinitionHashes { get; }

    public ResourceRegistry(ModuleRegistry modules, IEnumerable<IResourceDefinitionProvider> providers,
        IEnumerable<AnnotationVocabulary>? vocabularies = null)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(providers);
        var terms = ResourceValidation.Vocabulary(StandardAnnotations.Vocabularies.Concat(vocabularies ?? []).ToArray());
        var owners = modules.Descriptors.Select(descriptor => descriptor.Id).ToHashSet(StringComparer.Ordinal);
        var resources = new SortedDictionary<string, ResourceDefinition>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            // A module may implement the provider directly. Separate providers are explicit,
            // trusted composition; their declared owners must still exist in the catalog.
            string? owner = provider is IAlxarafeModule module ? modules.GetDescriptor(module).Id : null;
            foreach (var resource in provider.GetResourceDefinitions())
            {
                if (resource is null)
                    ResourceValidation.Fail("$registry", "provider", ResourceValidationCode.InvalidIdentity, "Provider returned a null definition.");
                if (resource.ModuleId is null || !owners.Contains(resource.ModuleId) || (owner is not null && owner != resource.ModuleId))
                    ResourceValidation.Fail(resource.Id, "moduleId", ResourceValidationCode.UnknownModule, "Unknown owner or contribution does not belong to its registered module.");
                ResourceValidation.Local(resource, terms);
                if (!resources.TryAdd(resource.Id, resource))
                    ResourceValidation.Fail(resource.Id, "id", ResourceValidationCode.Duplicate, "Resource ID is already registered.");
            }
        }
        foreach (var resource in resources.Values) ResourceValidation.Relations(resource, resources);
        _resources = new ReadOnlyDictionary<string, ResourceDefinition>(resources);
        All = Snapshot.Of(resources.Values);
        DefinitionHashes = new ReadOnlyDictionary<string, string>(resources.Values.ToDictionary(resource => resource.Id,
            resource => DefinitionHash.Compute(resource, terms), StringComparer.Ordinal));
    }

    public ResourceDefinition Require(string resourceId) => _resources.TryGetValue(resourceId, out var resource)
        ? resource : throw new KeyNotFoundException($"Resource '{resourceId}' is not registered.");
    public bool TryGet(string resourceId, out ResourceDefinition? resource) => _resources.TryGetValue(resourceId, out resource);
}
