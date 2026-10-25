namespace Alxarafe.Resources;

public enum UnsavedChangesPolicy { ConfirmDiscard, AllowDiscard }
public sealed record ResourceFormBehavior
{
    public UnsavedChangesPolicy UnsavedChanges { get; }
    public ResourceFormBehavior(UnsavedChangesPolicy unsavedChanges = UnsavedChangesPolicy.ConfirmDiscard) => UnsavedChanges = unsavedChanges;
}
public sealed record ResourcePresentationDefinition
{
    public ResourceFormDefinition Form { get; }
    public ResourcePresentationDefinition(ResourceFormDefinition form) => Form = form;
}
public sealed record ResourceFormDefinition
{
    public const int MaximumLabelLength = 200;
    public const int MaximumNodeIdLength = 100;
    public ResourceFormBehavior Behavior { get; }
    public IReadOnlyList<ResourceFormTab> Tabs { get; }
    public IReadOnlyList<string> Actions { get; }
    public ResourceFormDefinition(IEnumerable<ResourceFormTab> tabs, ResourceFormBehavior? behavior = null, IEnumerable<string>? actions = null)
    {
        Behavior = behavior ?? new(); Tabs = Snapshot.Of(tabs); Actions = Snapshot.Of(actions);
    }
}
public sealed record ResourceFormTab
{
    public string Id { get; }
    public string? Label { get; }
    public IReadOnlyList<ResourceFormSection> Sections { get; }
    public IReadOnlyList<string> Actions { get; }
    public ResourceFormTab(string id, IEnumerable<ResourceFormSection> sections, string? label = null, IEnumerable<string>? actions = null)
    {
        Id = id; Label = label; Sections = Snapshot.Of(sections); Actions = Snapshot.Of(actions);
    }
}
public sealed record ResourceFormSection
{
    public string Id { get; }
    public string? Label { get; }
    public bool Collapsible { get; }
    public bool InitiallyCollapsed { get; }
    public IReadOnlyList<ResourceFormFieldGroup> Groups { get; }
    public IReadOnlyList<string> Actions { get; }
    public ResourceFormSection(string id, IEnumerable<ResourceFormFieldGroup> groups, string? label = null,
        bool collapsible = false, bool initiallyCollapsed = false, IEnumerable<string>? actions = null)
    {
        Id = id; Label = label; Collapsible = collapsible; InitiallyCollapsed = initiallyCollapsed;
        Groups = Snapshot.Of(groups); Actions = Snapshot.Of(actions);
    }
}
public sealed record ResourceFormFieldGroup
{
    public string Id { get; }
    public string? Label { get; }
    public IReadOnlyList<string> Fields { get; }
    public ResourceFormFieldGroup(string id, IEnumerable<string> fields, string? label = null)
    {
        Id = id; Label = label; Fields = Snapshot.Of(fields);
    }
}
