namespace Alxarafe.Resources;

internal static class FormValidation
{
    internal static void Validate(ResourceDefinition resource)
    {
        if (resource.Presentation is null) return;
        var form = resource.Presentation.Form;
        if (form is null || form.Behavior is null || !Enum.IsDefined(form.Behavior.UnsavedChanges)) Reject("behavior", "Invalid form behavior.");
        if (form!.Tabs.Count == 0) Reject("tabs", "A form requires at least one tab.");
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
            if (tab.Sections.Count == 0) Reject(tabPath, "A tab requires sections.");
            ActionReferences(tab.Actions, tabPath + ".actions");
            Unique(tab.Sections, section => section.Id, tabPath + ".sections");
            foreach (var section in tab.Sections)
            {
                string sectionPath = tabPath + ".sections." + section.Id;
                Node(section.Id, section.Label, sectionPath);
                if (section.InitiallyCollapsed && !section.Collapsible) Reject(sectionPath, "Only collapsible sections can start collapsed.");
                if (section.Groups.Count == 0) Reject(sectionPath, "A section requires field groups.");
                ActionReferences(section.Actions, sectionPath + ".actions");
                Unique(section.Groups, group => group.Id, sectionPath + ".groups");
                foreach (var group in section.Groups)
                {
                    string groupPath = sectionPath + ".groups." + group.Id;
                    Node(group.Id, group.Label, groupPath);
                    if (group.Fields.Count == 0) Reject(groupPath, "A group requires field references.");
                    foreach (string name in group.Fields)
                        if (name is null || !fields.TryGetValue(name, out var field) || Excluded(field) || !placedFields.Add(name))
                            Reject(groupPath + ".fields", "Unknown, hidden, excluded or already placed field.");
                }
            }
        }
        foreach (var field in resource.Fields)
            if (!Excluded(field) && !placedFields.Contains(field.Name)) Reject("fields." + field.Name, "Every visible non-excluded field requires placement.");

        void Reject(string path, string detail) => ResourceValidation.Fail(resource.Id, "presentation.form." + path, ResourceValidationCode.InvalidPresentation, detail);
        void Unique<T>(IEnumerable<T> items, Func<T, string> id, string path)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
                if (item is null || !seen.Add(id(item))) Reject(path, "Null or duplicate layout node.");
        }
        void Node(string id, string? label, string path)
        {
            if (!ResourceValidation.Name(id) || id.Length > ResourceFormDefinition.MaximumNodeIdLength) Reject(path, "Invalid technical layout identity.");
            if (label is not null && (string.IsNullOrWhiteSpace(label) || label.Length > ResourceFormDefinition.MaximumLabelLength
                || !ResourceValidation.Text(label) || label.Any(char.IsControl) || label.Contains('<', StringComparison.Ordinal) || label.Contains('>', StringComparison.Ordinal)))
                Reject(path + ".label", "Labels must be bounded plain Unicode text without markup or controls.");
        }
        void ActionReferences(IEnumerable<string> names, string path)
        {
            foreach (string name in names)
                if (name is null || !actions.Contains(name) || !placedActions.Add(name)) Reject(path, "Unknown or already placed action.");
        }
    }
    private static bool Excluded(FieldDefinition field) => field.Annotations.Any(annotation =>
        (annotation.Key == StandardAnnotations.Hidden.Key || annotation.Key == StandardAnnotations.FormExcluded.Key)
        && annotation.Value is AnnotationBoolean { Value: true });
}
