using System.Diagnostics.CodeAnalysis;

namespace Alxarafe.Resources;

internal static class FormValidation
{
    internal static void Validate(ResourceDefinition resource)
    {
        if (resource.Presentation is not null)
            ValidateForm(resource, resource.Presentation.Form, "presentation.form", legacy: true, []);
        if (resource.ContextualPresentation is not { } contextual) return;
        if (contextual.Create is not null) ValidateForm(resource, contextual.Create, "contextualPresentation.create", legacy: false, [ActionScope.Collection]);
        if (contextual.Edit is not null) ValidateForm(resource, contextual.Edit, "contextualPresentation.edit", legacy: false, [ActionScope.Entity]);
        if (contextual.View is not null) ValidateForm(resource, contextual.View, "contextualPresentation.view", legacy: false, [ActionScope.Entity]);
        if (contextual.List is { } list)
        {
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var column in list.Columns)
            {
                if (column is null || !resource.Fields.Any(field => field.Name == column.Field) || !fields.Add(column.Field))
                    ResourceValidation.Fail(resource.Id, "contextualPresentation.list.columns", ResourceValidationCode.InvalidPresentation, "Unknown, null or duplicate list field.");
                Label(resource, column!.Label, "contextualPresentation.list.columns." + column.Field + ".label");
            }
            var actions = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in list.Actions)
            {
                var action = resource.Actions.SingleOrDefault(action => action.Name == name);
                if (action is null || !actions.Add(name) || action.Scope is not (ActionScope.Collection or ActionScope.Selection))
                    ResourceValidation.Fail(resource.Id, "contextualPresentation.list.actions", ResourceValidationCode.InvalidPresentation, "Unknown, duplicate or incompatible list action.");
            }
        }
    }
    private static void ValidateForm(ResourceDefinition resource, ResourceFormDefinition? form, string prefix, bool legacy, ActionScope[] scopes)
    {
        if (form is null || form.Behavior is null || !Enum.IsDefined(form.Behavior.UnsavedChanges)) Reject("behavior", "Invalid form behavior.");
        if (legacy && form.Tabs.Count == 0) Reject("tabs", "A form requires at least one tab.");
        var fields = resource.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        var actions = resource.Actions.Select(action => action.Name).ToHashSet(StringComparer.Ordinal);
        var placedFields = new HashSet<string>(StringComparer.Ordinal);
        var placedActions = new HashSet<string>(StringComparer.Ordinal);
        ActionReferences(form.Actions, "actions");
        Unique(form.Tabs, tab => tab.Id, "tabs");
        foreach (var tab in form.Tabs)
        {
            string tabPath = "tabs." + tab.Id;
            Node(tab.Id, tab.Label, tabPath);
            if (legacy && tab.Sections.Count == 0) Reject(tabPath, "A tab requires sections.");
            ActionReferences(tab.Actions, tabPath + ".actions");
            Unique(tab.Sections, section => section.Id, tabPath + ".sections");
            foreach (var section in tab.Sections)
            {
                string sectionPath = tabPath + ".sections." + section.Id;
                Node(section.Id, section.Label, sectionPath);
                if (section.InitiallyCollapsed && !section.Collapsible) Reject(sectionPath, "Only collapsible sections can start collapsed.");
                if (legacy && section.Groups.Count == 0) Reject(sectionPath, "A section requires field groups.");
                ActionReferences(section.Actions, sectionPath + ".actions");
                Unique(section.Groups, group => group.Id, sectionPath + ".groups");
                foreach (var group in section.Groups)
                {
                    string groupPath = sectionPath + ".groups." + group.Id;
                    Node(group.Id, group.Label, groupPath);
                    if (legacy && group.Fields.Count == 0) Reject(groupPath, "A group requires field references.");
                    foreach (string name in group.Fields)
                        if (name is null || !fields.TryGetValue(name, out var field) || (legacy && Excluded(field)) || !placedFields.Add(name))
                            Reject(groupPath + ".fields", "Unknown, hidden, excluded or already placed field.");
                }
            }
        }
        foreach (var field in resource.Fields)
            if (legacy && !Excluded(field) && !placedFields.Contains(field.Name)) Reject("fields." + field.Name, "Every visible non-excluded field requires placement.");

        [DoesNotReturn]
        void Reject(string path, string detail) => ResourceValidation.Fail(resource.Id, prefix + "." + path, ResourceValidationCode.InvalidPresentation, detail);
        void Unique<T>(IEnumerable<T> items, Func<T, string> id, string path)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
                if (item is null || !seen.Add(id(item))) Reject(path, "Null or duplicate layout node.");
        }
        void Node(string id, string? label, string path)
        {
            if (!ResourceValidation.Name(id) || id.Length > ResourceFormDefinition.MaximumNodeIdLength) Reject(path, "Invalid technical layout identity.");
            Label(resource, label, prefix + "." + path + ".label");
        }
        void ActionReferences(IEnumerable<string> names, string path)
        {
            foreach (string name in names)
                if (name is null || !actions.Contains(name) || !placedActions.Add(name)
                    || (scopes.Length != 0 && !scopes.Contains(resource.Actions.Single(action => action.Name == name).Scope)))
                    Reject(path, "Unknown, already placed or incompatible action.");
        }
    }
    private static void Label(ResourceDefinition resource, string? label, string path)
    {
        if (label is not null && (string.IsNullOrWhiteSpace(label) || label.Length > ResourceFormDefinition.MaximumLabelLength
            || !ResourceValidation.Text(label) || label.Any(char.IsControl) || label.Contains('<', StringComparison.Ordinal) || label.Contains('>', StringComparison.Ordinal)))
            ResourceValidation.Fail(resource.Id, path, ResourceValidationCode.InvalidPresentation, "Labels must be bounded plain Unicode text without markup or controls.");
    }
    private static bool Excluded(FieldDefinition field) => field.Annotations.Any(annotation =>
        (annotation.Key == StandardAnnotations.Hidden.Key || annotation.Key == StandardAnnotations.FormExcluded.Key)
        && annotation.Value is AnnotationBoolean { Value: true });
}
