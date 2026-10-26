using System.Globalization;
using System.Text.Json;
using Alxarafe.Modularity;
using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.Resources.Runtime;

namespace Alxarafe.Modules.Assets;

internal static class AssetDynamicSchema
{
    internal const int DefaultPrecision = 38;
    internal const int DefaultScale = 18;
    private const string ValidationResource = "assets.dynamic-field-schema";
    internal static readonly IReadOnlyList<ScalarType> Types = Array.AsReadOnly(new[]
        { ScalarType.String, ScalarType.Int32, ScalarType.Decimal, ScalarType.Boolean, ScalarType.Date });
    internal static string TypeName(ScalarType type) => Enum.GetName(type)!.ToUpperInvariant();
    internal static ScalarType Type(string name) => Types.Single(type => TypeName(type) == name);
    internal static string Slot(ScalarType type) => type switch
    {
        ScalarType.String => AssetFields.StringValue, ScalarType.Int32 => AssetFields.Int32Value,
        ScalarType.Decimal => AssetFields.DecimalValue, ScalarType.Boolean => AssetFields.BooleanValue,
        ScalarType.Date => AssetFields.DateValue, _ => throw new InvalidOperationException("Unsupported dynamic field type.")
    };
    internal static FieldDefinition Field(AssetFieldDefinitionRow row)
    {
        var range = new NumericConstraints(Parse(row.Minimum), Parse(row.Maximum));
        FieldConstraints? constraints = row.FieldType switch
        {
            ScalarType.String => new TextConstraints(row.MinimumLength, row.MaximumLength),
            ScalarType.Int32 => range,
            ScalarType.Decimal => new DecimalConstraints(row.Precision!.Value, row.Scale!.Value, range),
            _ => null
        };
        return new(Slot(row.FieldType), row.FieldType, required: row.Required, constraints: constraints);
    }
    internal static void Validate(AssetFieldDefinitionRow row, ModuleRegistry modules, bool create)
    {
        SafeText(row.Code, AssetFields.Code); SafeText(row.Name, AssetFields.Name);
        if (row.Description is { } description) SafeText(description, AssetFields.Description, nonblank: false);
        if (!Types.Contains(row.FieldType)) Invalid(AssetResources.FieldDefinition, AssetFields.FieldType);
        if (row.FieldType == ScalarType.Decimal && create)
        { row.Precision ??= DefaultPrecision; row.Scale ??= DefaultScale; }
        if (row.FieldType != ScalarType.String && (row.MinimumLength is not null || row.MaximumLength is not null))
            Invalid(AssetResources.FieldDefinition, AssetFields.MinimumLength);
        if (row.FieldType is not (ScalarType.Int32 or ScalarType.Decimal) && (row.Minimum is not null || row.Maximum is not null))
            Invalid(AssetResources.FieldDefinition, AssetFields.Minimum);
        if (row.FieldType != ScalarType.Decimal && (row.Precision is not null || row.Scale is not null))
            Invalid(AssetResources.FieldDefinition, AssetFields.Precision);
        if (row.FieldType == ScalarType.Decimal && (row.Precision is null or < 1 or > ResourceTransport.MaximumNumericDigits
            || row.Scale is null or < 0 or > ResourceTransport.MaximumNumericDigits)) Invalid(AssetResources.FieldDefinition, AssetFields.Precision);
        // Public semantic validation seam: validate this data-derived FieldDefinition
        // in isolation, without modifying the application's frozen ResourceRegistry.
        try { _ = new ResourceRegistry(modules, [new Probe(Field(row))]); }
        catch (ResourceValidationException) { Invalid(AssetResources.FieldDefinition, AssetFields.FieldType); }
    }
    private sealed class Probe(FieldDefinition field) : IResourceDefinitionProvider
    {
        public IReadOnlyCollection<ResourceDefinition> GetResourceDefinitions() =>
            [new(ValidationResource, AssetsModule.Version, AssetsModule.Id, new(AssetFields.Id),
                [new(AssetFields.Id, ScalarType.Uuid, readOnly: true, immutable: true), field], [], new([]))];
    }
    internal static ScalarValue Input(AssetFieldDefinitionRow definition, ResourceRecord input)
    {
        string expected = Slot(definition.FieldType);
        var supplied = Types.Select(Slot).Where(input.Values.ContainsKey).ToArray();
        if (supplied.Length != 1 || supplied[0] != expected) Invalid(AssetResources.FieldValue, expected);
        var value = input.Values[expected];
        if (value is TextValue text && text.Value.Contains(char.MinValue, StringComparison.Ordinal)) Invalid(AssetResources.FieldValue, expected);
        if (!ResourceValueValidator.IsValid(Field(definition), value)) Invalid(AssetResources.FieldValue, expected);
        return value;
    }
    internal static string Encode(ScalarValue value) => ResourceTransport.Project(value).GetRawText();
    internal static bool TryDecode(ScalarType type, string json, out ScalarValue value)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            value = ResourceTransport.Parse(type, document.RootElement);
            return true;
        }
        // The public canonical parser reports malformed scalars using transport
        // exceptions too. This catch surrounds only pure JSON/scalar parsing,
        // never IO, transactions, domain handlers or constraint evaluation.
        catch (Exception failure) when (failure is not (OutOfMemoryException or OperationCanceledException))
        { value = new NullValue(); return false; }
    }
    internal static bool Accepts(AssetFieldDefinitionRow definition, string json) =>
        TryDecode(definition.FieldType, json, out var value) && value is not NullValue
        && ResourceValueValidator.IsValid(Field(definition), value);
    internal static void Invalid(string resource, string field) => throw new ResourceRuntimeException(resource, ResourceRuntimeError.ValidationFailed,
        [new(field, ResourceInputError.InvalidValue, "Dynamic field input does not satisfy its definition.")]);
    private static void SafeText(string text, string field, bool nonblank = true)
    {
        if (text.Contains(char.MinValue, StringComparison.Ordinal) || (nonblank && string.IsNullOrWhiteSpace(text))) Invalid(AssetResources.FieldDefinition, field);
    }
    internal static ResourceNumber? Parse(string? value) => value is null ? null : ResourceNumber.Parse(value);
    private static int? Integer(ScalarValue value) => value is NumericValue number ? int.Parse(number.Value.CanonicalText, CultureInfo.InvariantCulture) : null;
    internal static void Patch(AssetFieldDefinitionRow row, ResourceRecord input)
    {
        foreach (var (field, value) in input.Values)
        {
            switch (field)
            {
                case AssetFields.Name: row.Name = ((TextValue)value).Value; break;
                case AssetFields.Description: row.Description = AssetsRecords.OptionalText(value); break;
                case AssetFields.Required: row.Required = ((BooleanValue)value).Value; break;
                case AssetFields.Active: row.Active = ((BooleanValue)value).Value; break;
                case AssetFields.DisplayOrder: row.DisplayOrder = Integer(value)!.Value; break;
                case AssetFields.MinimumLength: row.MinimumLength = Integer(value); break;
                case AssetFields.MaximumLength: row.MaximumLength = Integer(value); break;
                case AssetFields.Minimum: row.Minimum = value is NumericValue min ? min.Value.CanonicalText : null; break;
                case AssetFields.Maximum: row.Maximum = value is NumericValue max ? max.Value.CanonicalText : null; break;
                case AssetFields.Precision: row.Precision = Integer(value); break;
                case AssetFields.Scale: row.Scale = Integer(value); break;
            }
        }
    }
}
