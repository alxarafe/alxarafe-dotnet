namespace Alxarafe.Resources;

// Static declarations only. Null contexts are absent; empty collections are intentional.
public sealed record ResourceContextPresentationDefinition
{
    public ResourceListPresentationDefinition? List { get; }
    public ResourceFormDefinition? Create { get; }
    public ResourceFormDefinition? Edit { get; }
    public ResourceFormDefinition? View { get; }
    public ResourceContextPresentationDefinition(ResourceListPresentationDefinition? list = null,
        ResourceFormDefinition? create = null, ResourceFormDefinition? edit = null, ResourceFormDefinition? view = null)
    {
        List = list; Create = create; Edit = edit; View = view;
    }
}
public sealed record ResourceListPresentationDefinition
{
    public IReadOnlyList<ResourceListColumn> Columns { get; }
    public IReadOnlyList<string> Actions { get; }
    public ResourceListPresentationDefinition(IEnumerable<ResourceListColumn> columns, IEnumerable<string>? actions = null)
    {
        Columns = Snapshot.Of(columns); Actions = Snapshot.Of(actions);
    }
}
public sealed record ResourceListColumn
{
    public string Field { get; }
    public string? Label { get; }
    public ResourceListColumn(string field, string? label = null) { Field = field; Label = label; }
}
