using System.Text.Json.Serialization;

namespace Alxarafe.Resources.AspNetCore;

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
    private static ResourcePresentationDto? Presentation(ResourcePresentationDefinition? presentation) => presentation is null ? null :
        new(new(new(presentation.Form.Behavior.UnsavedChanges switch
        {
            UnsavedChangesPolicy.ConfirmDiscard => "CONFIRM_DISCARD",
            UnsavedChangesPolicy.AllowDiscard => "ALLOW_DISCARD",
            _ => throw new InvalidOperationException("Unsupported form behavior.")
        }), presentation.Form.Actions.ToArray(), presentation.Form.Tabs.Select(tab => new ResourceFormTabDto(tab.Id, tab.Label, tab.Actions.ToArray(),
            tab.Sections.Select(section => new ResourceFormSectionDto(section.Id, section.Label, section.Collapsible, section.InitiallyCollapsed,
                section.Actions.ToArray(), section.Groups.Select(group => new ResourceFormGroupDto(group.Id, group.Label, group.Fields.ToArray())).ToArray())).ToArray())).ToArray()));
}
