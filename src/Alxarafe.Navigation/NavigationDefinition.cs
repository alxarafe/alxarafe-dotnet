using System.Text.Json.Serialization;

namespace Alxarafe.Navigation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ResourceNavigationTarget), "RESOURCE")]
[JsonDerivedType(typeof(RouteNavigationTarget), "ROUTE")]
public abstract record NavigationTarget;
public sealed record ResourceNavigationTarget(string Resource) : NavigationTarget;
public sealed record RouteNavigationTarget(string Route) : NavigationTarget;

public sealed record NavigationDefinition
{
    public string Id { get; }
    public string ModuleId { get; }
    public string Label { get; }
    public int Order { get; }
    public string? ParentId { get; }
    public NavigationTarget? Target { get; }
    public string? Icon { get; }
    public IReadOnlyList<string> Permissions { get; }
    public bool RequiresTenant { get; }
    public Type? LabelResource { get; }
    public NavigationDefinition(string id, string moduleId, string label, int order = 0, string? parentId = null,
        NavigationTarget? target = null, string? icon = null, IEnumerable<string>? permissions = null,
        bool requiresTenant = false, Type? labelResource = null)
    {
        Id = id; ModuleId = moduleId; Label = label; Order = order; ParentId = parentId; Target = target; Icon = icon;
        Permissions = Array.AsReadOnly(permissions?.ToArray() ?? []); RequiresTenant = requiresTenant; LabelResource = labelResource;
    }
}
public interface INavigationDefinitionProvider
{
    IReadOnlyCollection<NavigationDefinition> GetNavigationDefinitions();
}
public sealed record NavigationItem(string Id, string Label, int Order, IReadOnlyList<NavigationItem> Children,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NavigationTarget? Target = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Icon = null);
public sealed record NavigationResponse(string Version, IReadOnlyList<NavigationItem> Items);
