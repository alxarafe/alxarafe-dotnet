namespace Alxarafe.Resources;

// Shared semantic value rules. Definitions must first pass ResourceRegistry validation;
// operation-specific write/required policies belong to Resource Runtime.
public static class ResourceValueValidator
{
    public static bool IsValid(ActionParameterDefinition parameter, ScalarValue value) => IsValid(parameter.Field, value);
    public static bool IsValid(FieldDefinition field, ScalarValue value) => value is not null && ResourceValidation.Accepts("$value", field, value);
    public static EffectiveNumericConstraints? NumericConstraints(FieldDefinition field) => NumericDomain.Intrinsic(field.Type) is null
        ? null : ResourceValidation.Numeric("$value", field);
    public static bool IsText(string? value) => ResourceValidation.Text(value);
}
