using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Alxarafe.Resources;

// Deliberate semantic projection, not reflection/object serialization. Format changes
// require a new canonical format identifier and golden-vector review.
internal static class DefinitionHash
{
    private const string CanonicalFormat = "erbas.resource-definition.v1";
    internal static string Compute(ResourceDefinition resource, IReadOnlyDictionary<string, AnnotationTerm> terms)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("format", CanonicalFormat);
            writer.WriteString("id", resource.Id);
            writer.WriteString("version", resource.Version);
            writer.WriteString("moduleId", resource.ModuleId);
            writer.WriteString("idField", resource.IdDefinition.Field);
            writer.WriteStartArray("fields");
            foreach (var field in resource.Fields.OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("name", field.Name);
                writer.WriteString("type", Enum.GetName(field.Type));
                writer.WriteBoolean("nullable", field.Nullable);
                writer.WriteBoolean("required", field.Required);
                writer.WriteBoolean("readOnly", field.ReadOnly);
                writer.WriteBoolean("immutable", field.Immutable);
                writer.WriteBoolean("filterable", field.Capabilities.Filterable);
                writer.WriteBoolean("sortable", field.Capabilities.Sortable);
                writer.WriteBoolean("searchable", field.Capabilities.Searchable);
                writer.WritePropertyName("default");
                if (field.DefaultValue is null) writer.WriteNullValue();
                else
                {
                    writer.WriteStartObject();
                    switch (field.DefaultValue)
                    {
                        case NullValue: writer.WriteNull("null"); break;
                        case BooleanValue boolean: writer.WriteBoolean("boolean", boolean.Value); break;
                        case TextValue text: writer.WriteString("text", text.Value); break;
                        case NumericValue number: writer.WriteString("number", number.Value.CanonicalText); break;
                        case UuidValue uuid: writer.WriteString("uuid", uuid.Value.ToString("D", CultureInfo.InvariantCulture)); break;
                        case DateValue date: writer.WriteString("date", date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); break;
                        case TimeValue time: writer.WriteString("time", time.Value.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)); break;
                        case DateTimeValue instant: writer.WriteString("instant", instant.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)); break;
                        case BinaryValue binary: writer.WriteString("binary", Convert.ToBase64String(Convert.FromBase64String(binary.Base64))); break;
                    }
                    writer.WriteEndObject();
                }
                writer.WriteStartArray("enumValues");
                foreach (string value in field.EnumValues.Order(StringComparer.Ordinal)) writer.WriteStringValue(value);
                writer.WriteEndArray();
                writer.WriteStartObject("constraints");
                if (NumericDomain.Intrinsic(field.Type) is not null)
                {
                    var shape = ResourceValidation.Numeric(resource.Id, field);
                    writer.WriteString("minimum", shape.Minimum?.CanonicalText);
                    writer.WriteString("maximum", shape.Maximum?.CanonicalText);
                    writer.WriteBoolean("exclusiveMinimum", shape.ExclusiveMinimum);
                    writer.WriteBoolean("exclusiveMaximum", shape.ExclusiveMaximum);
                    writer.WriteString("step", shape.Step?.CanonicalText);
                    if (shape.Precision is { } precision) writer.WriteNumber("precision", precision);
                    if (shape.Scale is { } scale) writer.WriteNumber("scale", scale);
                }
                else if (field.Type is ScalarType.String or ScalarType.Text)
                {
                    var text = (TextConstraints?)field.Constraints;
                    writer.WriteNumber("minimumLength", text?.MinimumLength ?? 0);
                    if (text?.MaximumLength is { } maximumLength) writer.WriteNumber("maximumLength", maximumLength);
                    writer.WriteString("pattern", text?.Pattern);
                }
                writer.WriteEndObject();
                Annotations(writer, field.Annotations, terms);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("relations");
            foreach (var relation in resource.Relations.OrderBy(relation => relation.Name, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("name", relation.Name);
                writer.WriteString("kind", Enum.GetName(relation.Kind));
                writer.WriteString("targetResource", relation.TargetResource);
                writer.WriteString("localField", relation.LocalField);
                writer.WriteString("targetField", relation.TargetField);
                writer.WriteString("displayField", relation.DisplayField);
                writer.WriteBoolean("required", relation.Required);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("operations");
            foreach (var rule in resource.Security.Rules.OrderBy(rule => rule.Operation))
            {
                writer.WriteStartObject();
                writer.WriteString("operation", Enum.GetName(rule.Operation));
                writer.WriteStartArray("permissions");
                foreach (string permission in rule.Permissions.Order(StringComparer.Ordinal)) writer.WriteStringValue(permission);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("actions");
            foreach (var action in resource.Actions.OrderBy(action => action.Name, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("name", action.Name);
                writer.WriteString("scope", Enum.GetName(action.Scope));
                writer.WriteString("permission", action.Permission);
                writer.WriteBoolean("requiresSelection", action.RequiresSelection);
                Annotations(writer, action.Annotations, terms);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            Annotations(writer, resource.Annotations, terms);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void Annotations(Utf8JsonWriter writer, IEnumerable<Annotation> annotations, IReadOnlyDictionary<string, AnnotationTerm> terms)
    {
        writer.WriteStartArray("annotations");
        foreach (var annotation in annotations.OrderBy(annotation => annotation.Key, StringComparer.Ordinal))
        {
            var term = terms[annotation.Key];
            writer.WriteStartObject();
            writer.WriteString("key", annotation.Key);
            writer.WriteString("kind", Enum.GetName(term.Kind));
            writer.WriteNumber("targets", (int)term.Targets);
            writer.WritePropertyName("value");
            switch (annotation.Value)
            {
                case AnnotationText text: writer.WriteStringValue(text.Value); break;
                case AnnotationBoolean boolean: writer.WriteBooleanValue(boolean.Value); break;
                case AnnotationNumber number: writer.WriteStringValue(number.Value.CanonicalText); break;
                case AnnotationFieldNames fields:
                    writer.WriteStartArray();
                    foreach (string field in fields.Fields) writer.WriteStringValue(field);
                    writer.WriteEndArray();
                    break;
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
