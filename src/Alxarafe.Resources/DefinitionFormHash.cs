using System.Text.Json;

namespace Alxarafe.Resources;

internal static partial class DefinitionHash
{
    private static void Presentation(Utf8JsonWriter writer, ResourcePresentationDefinition? presentation)
    {
        writer.WritePropertyName("presentation");
        if (presentation is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WritePropertyName("form");
        Form(writer, presentation.Form);
        writer.WriteEndObject();
    }
    private static void Form(Utf8JsonWriter writer, ResourceFormDefinition form)
    {
        writer.WriteStartObject();
        writer.WriteString("unsavedChanges", Enum.GetName(form.Behavior.UnsavedChanges));
        References(writer, "actions", form.Actions);
        writer.WriteStartArray("tabs");
        foreach (var tab in form.Tabs)
        {
            writer.WriteStartObject();
            writer.WriteString("id", tab.Id); writer.WriteString("label", tab.Label);
            References(writer, "actions", tab.Actions);
            writer.WriteStartArray("sections");
            foreach (var section in tab.Sections)
            {
                writer.WriteStartObject();
                writer.WriteString("id", section.Id); writer.WriteString("label", section.Label);
                writer.WriteBoolean("collapsible", section.Collapsible); writer.WriteBoolean("initiallyCollapsed", section.InitiallyCollapsed);
                References(writer, "actions", section.Actions);
                writer.WriteStartArray("groups");
                foreach (var group in section.Groups)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", group.Id); writer.WriteString("label", group.Label);
                    References(writer, "fields", group.Fields);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
    private static void ContextualPresentation(Utf8JsonWriter writer, ResourceContextPresentationDefinition contextual)
    {
        writer.WriteStartObject("contextualPresentation");
        if (contextual.List is { } list)
        {
            writer.WriteStartObject("list");
            writer.WriteStartArray("columns");
            foreach (var column in list.Columns)
            {
                writer.WriteStartObject(); writer.WriteString("field", column.Field); writer.WriteString("label", column.Label); writer.WriteEndObject();
            }
            writer.WriteEndArray(); References(writer, "actions", list.Actions); writer.WriteEndObject();
        }
        if (contextual.Create is { } create) { writer.WritePropertyName("create"); Form(writer, create); }
        if (contextual.Edit is { } edit) { writer.WritePropertyName("edit"); Form(writer, edit); }
        if (contextual.View is { } view) { writer.WritePropertyName("view"); Form(writer, view); }
        writer.WriteEndObject();
    }
    private static void References(Utf8JsonWriter writer, string property, IReadOnlyList<string> references)
    {
        writer.WriteStartArray(property);
        foreach (string reference in references) writer.WriteStringValue(reference);
        writer.WriteEndArray();
    }
}
