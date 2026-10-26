using System.Text.RegularExpressions;
using Alxarafe.Modularity;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;

namespace Alxarafe.Navigation;

public sealed class NavigationRegistry
{
    private static readonly Regex Identifier = new(@"\A[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex LocalRoute = new(@"\A/(?:[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*/?)?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public IReadOnlyList<NavigationDefinition> All { get; }
    public NavigationRegistry(ModuleRegistry modules, ResourceRegistry resources, ResourceRepositoryCatalog repositories, IEnumerable<INavigationDefinitionProvider> providers)
    {
        var owners = modules.Descriptors.Select(module => module.Id).ToHashSet(StringComparer.Ordinal);
        var items = new Dictionary<string, NavigationDefinition>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            string? owner = provider is IAlxarafeModule module ? modules.GetDescriptor(module).Id : null;
            foreach (var item in provider.GetNavigationDefinitions())
            {
                if (item is null || !owners.Contains(item.ModuleId) || (owner is not null && owner != item.ModuleId)
                    || string.IsNullOrEmpty(item.Id) || !Identifier.IsMatch(item.Id) || !item.Id.StartsWith(item.ModuleId + ".", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(item.Label) || !ResourceValueValidator.IsText(item.Label)
                    || (item.Icon is not null && (string.IsNullOrWhiteSpace(item.Icon) || !ResourceValueValidator.IsText(item.Icon)))
                    || item.Permissions.Any(permission => !Identifier.IsMatch(permission))
                    || item.Permissions.Distinct(StringComparer.Ordinal).Count() != item.Permissions.Count
                    || !items.TryAdd(item.Id, item)) throw new InvalidOperationException("Invalid navigation declaration.");
                switch (item.Target)
                {
                    case null: break;
                    case ResourceNavigationTarget target:
                        if (string.IsNullOrWhiteSpace(target.Resource) || !resources.TryGet(target.Resource, out var resource) || !resource!.Operations.Contains(ResourceOperation.List)
                            || !repositories.Contains(target.Resource)) throw new InvalidOperationException("Navigation resource target must support bound list access.");
                        break;
                    case RouteNavigationTarget target:
                        if (string.IsNullOrWhiteSpace(target.Route) || !LocalRoute.IsMatch(target.Route) || item.Permissions.Count == 0) throw new InvalidOperationException("Navigation route requires a local application path and explicit authorization.");
                        break;
                    default: throw new InvalidOperationException("Unknown navigation target.");
                }
            }
        }
        foreach (var item in items.Values)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { item.Id };
            var current = item;
            while (current.ParentId is { } parentId)
            {
                if (!items.TryGetValue(parentId, out var parent) || parent.ModuleId != current.ModuleId || parent.Target is not null || !visited.Add(parentId))
                    throw new InvalidOperationException("Invalid navigation parent or cycle.");
                current = parent;
            }
        }
        All = Array.AsReadOnly(items.Values.OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray());
    }
}
