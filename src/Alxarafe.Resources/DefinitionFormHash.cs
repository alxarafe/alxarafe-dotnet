using System.Text.Json;

namespace Alxarafe.Resources;

internal static partial class DefinitionHash
{
    private static void Presentation(Utf8JsonWriter writer, ResourcePresentationDefinition? presentation)
    {
        writer.WritePropertyName("presentation");
        if (presentation is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteStartObject("form");
        writer.WriteString("unsavedChanges", Enum.GetName(presentation.Form.Behavior.UnsavedChanges));
        References(writer, "actions", presentation.Form.Actions);
        writer.WriteStartArray("tabs");
        foreach (var tab in presentation.Form.Tabs)
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
        writer.WriteEndObject();
    }
    private static void References(Utf8JsonWriter writer, string property, IReadOnlyList<string> references)
    {
        writer.WriteStartArray(property);
        foreach (string reference in references) writer.WriteStringValue(reference);
        writer.WriteEndArray();
    }
}
