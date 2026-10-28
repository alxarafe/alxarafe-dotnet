using System.Text.Json.Serialization;

namespace Alxarafe.Resources.AspNetCore;

public sealed record ResourceContextPresentationDto(string Id, string DefinitionHash, ResourceContextPresentationBodyDto Presentation);
public sealed record ResourceContextPresentationBodyDto(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ResourceListPresentationDto? List,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ResourceFormDto? Create,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ResourceFormDto? Edit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ResourceFormDto? View);
public sealed record ResourceListPresentationDto(IReadOnlyList<ResourceListColumnDto> Columns, IReadOnlyList<string> Actions);
public sealed record ResourceListColumnDto(string Field,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label);
public sealed record ResourcePresentationDto(ResourceFormDto Form);
public sealed record ResourceFormDto(ResourceFormBehaviorDto Behavior, IReadOnlyList<string> Actions, IReadOnlyList<ResourceFormTabDto> Tabs);
public sealed record ResourceFormBehaviorDto(string UnsavedChanges);
public sealed record ResourceFormTabDto(string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label,
    IReadOnlyList<string> Actions, IReadOnlyList<ResourceFormSectionDto> Sections);
public sealed record ResourceFormSectionDto(string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label,
    bool Collapsible, bool InitiallyCollapsed, IReadOnlyList<string> Actions, IReadOnlyList<ResourceFormGroupDto> Groups);
public sealed record ResourceFormGroupDto(string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label, IReadOnlyList<string> Fields);

public sealed partial class ResourceMetadataProjection
{
    public ResourceContextPresentationDto ContextPresentation(ResourceDefinition resource)
    {
        var contextual = resource.ContextualPresentation;
        return new(resource.Id, resources.DefinitionHashes[resource.Id], new(
            contextual?.List is { } list ? new(list.Columns.Select(column => new ResourceListColumnDto(column.Field, column.Label)).ToArray(), list.Actions.ToArray()) : null,
            contextual?.Create is { } create ? Form(create) : null,
            contextual?.Edit is { } edit ? Form(edit) : null,
            contextual?.View is { } view ? Form(view) : null));
    }
    private static ResourcePresentationDto? Presentation(ResourcePresentationDefinition? presentation) => presentation is null ? null : new(Form(presentation.Form));
    private static ResourceFormDto Form(ResourceFormDefinition form) => new(new(form.Behavior.UnsavedChanges switch
    {
        UnsavedChangesPolicy.ConfirmDiscard => "CONFIRM_DISCARD",
        UnsavedChangesPolicy.AllowDiscard => "ALLOW_DISCARD",
        _ => throw new InvalidOperationException("Unsupported form behavior.")
    }), form.Actions.ToArray(), form.Tabs.Select(tab => new ResourceFormTabDto(tab.Id, tab.Label, tab.Actions.ToArray(),
        tab.Sections.Select(section => new ResourceFormSectionDto(section.Id, section.Label, section.Collapsible, section.InitiallyCollapsed,
            section.Actions.ToArray(), section.Groups.Select(group => new ResourceFormGroupDto(group.Id, group.Label, group.Fields.ToArray())).ToArray())).ToArray())).ToArray());
}
