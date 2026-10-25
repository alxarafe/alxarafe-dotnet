using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Alxarafe.Resources;

public enum ResourceValidationCode
{
    InvalidIdentity, Duplicate, UnknownModule, InvalidField, InvalidConstraint,
    InvalidDefault, InvalidSecurity, InvalidRelation, InvalidAction, InvalidAnnotation, InvalidPresentation
}

public sealed class ResourceValidationException(string resourceId, string path, ResourceValidationCode code, string detail)
    : InvalidOperationException($"Resource '{resourceId}' at '{path}': {code}: {detail}")
{
    public string ResourceId { get; } = resourceId;
    public string Path { get; } = path;
    public ResourceValidationCode Code { get; } = code;
}

public sealed record EffectiveNumericConstraints(ResourceNumber? Minimum, ResourceNumber? Maximum,
    bool ExclusiveMinimum, bool ExclusiveMaximum, ResourceNumber? Step, int? Precision, int? Scale);

internal static class ResourceValidation
{
    private const string NamePattern = @"\A[A-Za-z][A-Za-z0-9_]*\z";
    private const string IdentifierPattern = @"\A[a-z][a-z0-9]*(?:[-_][a-z0-9]+)*(?:\.[a-z][a-z0-9]*(?:[-_][a-z0-9]+)*)*\z";
    private static readonly Regex Names = new(NamePattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Identifiers = new(IdentifierPattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);
    internal const string VocabularyContext = "$vocabularies";
    internal static bool Name(string? value) => value is not null && Names.IsMatch(value);
    internal static bool Identifier(string? value) => value is not null && Identifiers.IsMatch(value);
    private static bool Permission(string? value) => Identifier(value) && value!.Contains('.', StringComparison.Ordinal);
    internal static bool Text(string? value)
    {
        if (value is null) return false;
        try { StrictUtf8.GetByteCount(value); return true; }
        catch (EncoderFallbackException) { return false; }
    }

    [DoesNotReturn]
    internal static void Fail(string id, string path, ResourceValidationCode code, string detail) =>
        throw new ResourceValidationException(id, path, code, detail);

    internal static void Unique<T>(IEnumerable<T> values, Func<T, string> key, string id, string path)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in values)
            if (item is null || key(item) is not { } name || !seen.Add(name))
                Fail(id, path, ResourceValidationCode.Duplicate, "Null or duplicate declaration.");
    }

    internal static void Local(ResourceDefinition resource, IReadOnlyDictionary<string, AnnotationTerm> terms)
    {
        string id = resource.Id;
        if (!Identifier(id) || !System.Version.TryParse(resource.Version, out var version)
            || version.Build < 0 || version.Revision >= 0 || version.ToString() != resource.Version)
            Fail(id, "identity", ResourceValidationCode.InvalidIdentity, "Expected a stable resource ID and numeric major.minor.patch version.");
        Unique(resource.Fields, field => field.Name, id, "fields");
        foreach (var field in resource.Fields.OrderBy(field => field.Name, StringComparer.Ordinal)) Field(resource, field, terms);
        var identifier = resource.Fields.SingleOrDefault(field => field.Name == resource.IdDefinition?.Field);
        if (identifier is null || identifier.Nullable || !identifier.Immutable
            || identifier.Type is not (ScalarType.Uuid or ScalarType.String or ScalarType.Int8 or ScalarType.UInt8
                or ScalarType.Int16 or ScalarType.UInt16 or ScalarType.Int32 or ScalarType.UInt32 or ScalarType.Int64))
            Fail(id, "idDefinition", ResourceValidationCode.InvalidField, "ID must name one non-nullable immutable UUID, string or integer field.");
        if (!identifier.ReadOnly && !identifier.Required)
            Fail(id, "idDefinition", ResourceValidationCode.InvalidField, "Client-assigned IDs must be required; server-assigned IDs must be read-only.");
        if (resource.Operations.Any(operation => !Enum.IsDefined(operation))
            || resource.Operations.Distinct().Count() != resource.Operations.Count || resource.Security is null)
            Fail(id, "operations", ResourceValidationCode.InvalidSecurity, "Invalid or duplicate operation declaration, or missing security metadata.");
        var rules = resource.Security.Rules;
        if (rules.Any(rule => rule is null || !Enum.IsDefined(rule.Operation))
            || rules.Select(rule => rule.Operation).Distinct().Count() != rules.Count
            || !resource.Operations.Order().SequenceEqual(rules.Select(rule => rule.Operation).Order()))
            Fail(id, "security", ResourceValidationCode.InvalidSecurity, "Exactly one security rule is required for every supported operation, and none for unsupported operations.");
        foreach (var rule in rules)
            if (rule.Permissions.Count == 0 || rule.Permissions.Any(permission => !Permission(permission))
                || rule.Permissions.Distinct(StringComparer.Ordinal).Count() != rule.Permissions.Count)
                Fail(id, "security." + rule.Operation, ResourceValidationCode.InvalidSecurity, "Permissions must be distinct nonempty dotted technical identifiers; all are required.");
        Unique(resource.Relations, relation => relation.Name, id, "relations");
        foreach (var relation in resource.Relations)
            if (!Name(relation.Name) || !Enum.IsDefined(relation.Kind) || !Identifier(relation.TargetResource)
                || !Name(relation.LocalField) || !Name(relation.TargetField) || (relation.DisplayField is not null && !Name(relation.DisplayField)))
                Fail(id, "relations." + relation.Name, ResourceValidationCode.InvalidRelation, "Malformed relation declaration.");
        Unique(resource.Actions, action => action.Name, id, "actions");
        foreach (var action in resource.Actions)
        {
            if (!Identifier(action.Name) || !Enum.IsDefined(action.Scope) || !Permission(action.Permission)
                || action.RequiresSelection != (action.Scope == ActionScope.Selection))
                Fail(id, "actions." + action.Name, ResourceValidationCode.InvalidAction, "Invalid action identity, permission, scope or selection requirement.");
            Annotations(resource, action.Annotations, AnnotationTarget.Action, terms, "actions." + action.Name);
            Unique(action.Parameters, parameter => parameter.Name, id, "actions." + action.Name + ".parameters");
            foreach (var parameter in action.Parameters)
                Field(resource, parameter.Field, terms, AnnotationTarget.ActionParameter, "actions." + action.Name + ".parameters." + parameter.Name);
        }
        Annotations(resource, resource.Annotations, AnnotationTarget.Resource, terms, "annotations");
        FormValidation.Validate(resource);
    }

    private static void Field(ResourceDefinition resource, FieldDefinition field, IReadOnlyDictionary<string, AnnotationTerm> terms,
        AnnotationTarget target = AnnotationTarget.Field, string? path = null)
    {
        path ??= "fields." + field.Name;
        if (!Name(field.Name) || !Enum.IsDefined(field.Type) || (field.Required && (field.Nullable || field.ReadOnly))
            || (field.Capabilities.Searchable && field.Type is not (ScalarType.String or ScalarType.Text)))
            Fail(resource.Id, path, ResourceValidationCode.InvalidField, "Invalid name/type, contradictory input flags or non-text search capability.");
        if (field.Type == ScalarType.Enum)
        {
            if (field.EnumValues.Count == 0 || field.EnumValues.Any(value => string.IsNullOrWhiteSpace(value) || !Text(value))
                || field.EnumValues.Distinct(StringComparer.Ordinal).Count() != field.EnumValues.Count)
                Fail(resource.Id, path, ResourceValidationCode.InvalidField, "Enum fields require distinct nonempty text values.");
        }
        else if (field.EnumValues.Count != 0)
            Fail(resource.Id, path, ResourceValidationCode.InvalidField, "Only enum fields declare enum values.");
        if (NumericDomain.Intrinsic(field.Type) is not null) Numeric(resource.Id, field);
        else if (field.Type is ScalarType.String or ScalarType.Text)
        {
            if (field.Constraints is not null and not TextConstraints)
                Fail(resource.Id, path, ResourceValidationCode.InvalidConstraint, "Text fields require text constraints.");
            var constraints = (TextConstraints?)field.Constraints;
            if (constraints is not null)
            {
                if (constraints.MinimumLength < 0 || constraints.MaximumLength < 0 || constraints.MaximumLength < constraints.MinimumLength)
                    Fail(resource.Id, path, ResourceValidationCode.InvalidConstraint, "Impossible text length interval.");
                if (constraints.Pattern is { } pattern)
                {
                    try { Pattern(pattern); }
                    catch (ArgumentException) { Fail(resource.Id, path, ResourceValidationCode.InvalidConstraint, "Malformed pattern."); }
                    catch (NotSupportedException) { Fail(resource.Id, path, ResourceValidationCode.InvalidConstraint, "Pattern is outside the supported non-backtracking subset."); }
                }
            }
        }
        else if (field.Constraints is not null)
            Fail(resource.Id, path, ResourceValidationCode.InvalidConstraint, "Constraints are incompatible with the semantic type.");
        if (field.DefaultValue is not null && !Accepts(resource.Id, field, field.DefaultValue))
            Fail(resource.Id, path + ".defaultValue", ResourceValidationCode.InvalidDefault, "Default is incompatible with the type or effective constraints.");
        Annotations(resource, field.Annotations, target, terms, path + ".annotations");
    }

    private static Regex Pattern(string pattern) => new(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, PatternTimeout);

    internal static EffectiveNumericConstraints Numeric(string id, FieldDefinition field)
    {
        string path = "fields." + field.Name + ".constraints";
        var intrinsic = NumericDomain.Intrinsic(field.Type)!;
        var minimum = intrinsic.Minimum;
        var maximum = intrinsic.Maximum;
        var step = intrinsic.Step;
        int? precision = null, scale = null;
        NumericConstraints? range = null;
        if (field.Constraints is DecimalConstraints fixedPoint && field.Type == ScalarType.Decimal)
        {
            if (fixedPoint.Precision <= 0 || fixedPoint.Scale < 0 || fixedPoint.Scale > fixedPoint.Precision)
                Fail(id, path, ResourceValidationCode.InvalidConstraint, "Decimal precision must be positive and scale between zero and precision.");
            precision = fixedPoint.Precision; scale = fixedPoint.Scale;
            maximum = new(BigInteger.Pow(ResourceNumber.Radix, fixedPoint.Precision) - BigInteger.One, fixedPoint.Scale);
            minimum = new(-maximum.Coefficient, maximum.Scale);
            step = new(BigInteger.One, fixedPoint.Scale);
            range = fixedPoint.Range;
        }
        else if (field.Constraints is NumericConstraints numeric) range = numeric;
        else if (field.Constraints is not null)
            Fail(id, path, ResourceValidationCode.InvalidConstraint, "Numeric constraints required; fixed precision belongs only to Decimal.");
        if (range is not null)
        {
            if ((range.ExclusiveMinimum && range.Minimum is null) || (range.ExclusiveMaximum && range.Maximum is null)
                || (range.MultipleOf is not null && range.MultipleOf.CompareTo(ResourceNumber.Zero) <= 0))
                Fail(id, path, ResourceValidationCode.InvalidConstraint, "Exclusive bounds need a value; multipleOf must be positive.");
            foreach (var bound in new[] { range.Minimum, range.Maximum }.OfType<ResourceNumber>())
                if ((minimum is not null && bound.CompareTo(minimum) < 0) || (maximum is not null && bound.CompareTo(maximum) > 0))
                    Fail(id, path, ResourceValidationCode.InvalidConstraint, "Field bounds may restrict but never widen the intrinsic domain.");
            minimum = range.Minimum ?? minimum; maximum = range.Maximum ?? maximum;
            if (range.MultipleOf is { } multiple) step = step is null ? multiple : ResourceNumber.CommonStep(step, multiple);
        }
        bool exclusiveMinimum = range?.ExclusiveMinimum ?? false, exclusiveMaximum = range?.ExclusiveMaximum ?? false;
        if (minimum is not null && maximum is not null)
        {
            int comparison = minimum.CompareTo(maximum);
            if (comparison > 0 || (comparison == 0 && (exclusiveMinimum || exclusiveMaximum)))
                Fail(id, path, ResourceValidationCode.InvalidConstraint, "Empty numeric interval.");
            if (step is not null)
            {
                var first = minimum.FloorQuotient(step);
                if (exclusiveMinimum || !minimum.IsMultipleOf(step)) first++;
                var last = maximum.FloorQuotient(step);
                if (exclusiveMaximum && maximum.IsMultipleOf(step)) last--;
                if (first > last) Fail(id, path, ResourceValidationCode.InvalidConstraint, "No representable value satisfies the interval and multipleOf.");
            }
        }
        // Canonicalize the intersection to its lattice; equivalent bounds hash identically.
        if (step is not null)
        {
            if (minimum is not null)
            {
                var first = minimum.FloorQuotient(step);
                if (exclusiveMinimum || !minimum.IsMultipleOf(step)) first++;
                minimum = new(first * step.Coefficient, step.Scale);
            }
            if (maximum is not null)
            {
                var last = maximum.FloorQuotient(step);
                if (exclusiveMaximum && maximum.IsMultipleOf(step)) last--;
                maximum = new(last * step.Coefficient, step.Scale);
            }
            exclusiveMinimum = false; exclusiveMaximum = false;
        }
        return new(minimum, maximum, exclusiveMinimum, exclusiveMaximum, step, precision, scale);
    }

    internal static bool Accepts(string id, FieldDefinition field, ScalarValue value)
    {
        if (value is NullValue) return field.Nullable;
        if (NumericDomain.Intrinsic(field.Type) is not null)
        {
            if (value is not NumericValue { Value: { } number }) return false;
            if (field.Type != ScalarType.Decimal && !number.IsInteger) return false;
            var range = Numeric(id, field);
            if (range.Minimum is { } min && (number.CompareTo(min) < 0 || (range.ExclusiveMinimum && number.CompareTo(min) == 0))) return false;
            if (range.Maximum is { } max && (number.CompareTo(max) > 0 || (range.ExclusiveMaximum && number.CompareTo(max) == 0))) return false;
            return (range.Step is null || number.IsMultipleOf(range.Step)) && (range.Scale is null || number.Scale <= range.Scale);
        }
        if (field.Type is ScalarType.String or ScalarType.Text)
        {
            if (value is not TextValue text || !Text(text.Value)) return false;
            int length = text.Value.EnumerateRunes().Count();
            var constraints = (TextConstraints?)field.Constraints;
            if (length < (constraints?.MinimumLength ?? 0) || length > constraints?.MaximumLength) return false;
            try { return constraints?.Pattern is not { } pattern || Pattern(pattern).IsMatch(text.Value); }
            catch (RegexMatchTimeoutException) { return false; }
        }
        return field.Type switch
        {
            ScalarType.Boolean => value is BooleanValue,
            ScalarType.Uuid => value is UuidValue,
            ScalarType.Date => value is DateValue,
            ScalarType.Time => value is TimeValue,
            ScalarType.DateTime => value is DateTimeValue,
            ScalarType.Enum => value is TextValue text && field.EnumValues.Contains(text.Value, StringComparer.Ordinal),
            ScalarType.Binary => value is BinaryValue binary && ValidBase64(binary.Base64),
            _ => false
        };
    }

    private static bool ValidBase64(string? value)
    {
        if (value is null) return false;
        try { Convert.FromBase64String(value); return true; }
        catch (FormatException) { return false; }
    }

    internal static IReadOnlyDictionary<string, AnnotationTerm> Vocabulary(IEnumerable<AnnotationVocabulary> vocabularies)
    {
        var terms = new Dictionary<string, AnnotationTerm>(StringComparer.Ordinal);
        Unique(vocabularies, vocabulary => vocabulary.Name, VocabularyContext, "vocabularies");
        const AnnotationTarget targets = AnnotationTarget.Resource | AnnotationTarget.Field | AnnotationTarget.Action | AnnotationTarget.ActionParameter;
        foreach (var vocabulary in vocabularies)
        {
            if (!Name(vocabulary.Name)) Fail(VocabularyContext, "name", ResourceValidationCode.InvalidAnnotation, "Invalid vocabulary name.");
            foreach (var term in vocabulary.Terms)
                if (term is null || term.Vocabulary != vocabulary.Name || !Name(term.Name) || !Enum.IsDefined(term.Kind)
                    || term.Targets == 0 || (term.Targets & ~targets) != 0 || !terms.TryAdd(term.Key, term))
                    Fail(VocabularyContext, vocabulary.Name, ResourceValidationCode.InvalidAnnotation, "Invalid or duplicate typed annotation term.");
        }
        return terms;
    }

    private static void Annotations(ResourceDefinition resource, IReadOnlyList<Annotation> annotations, AnnotationTarget target,
        IReadOnlyDictionary<string, AnnotationTerm> terms, string path)
    {
        Unique(annotations, annotation => annotation.Key, resource.Id, path);
        foreach (var annotation in annotations)
        {
            if (!terms.TryGetValue(annotation.Key, out var term) || !term.Targets.HasFlag(target))
                Fail(resource.Id, path, ResourceValidationCode.InvalidAnnotation, "Unknown annotation term or unsupported target.");
            bool valid = (term.Kind, annotation.Value) switch
            {
                (AnnotationValueKind.Text, AnnotationText text) => Text(text.Value),
                (AnnotationValueKind.Boolean, AnnotationBoolean) => true,
                (AnnotationValueKind.Number, AnnotationNumber number) => number.Value is not null,
                (AnnotationValueKind.FieldNames, AnnotationFieldNames names) => names.Fields.Count > 0
                    && names.Fields.Distinct(StringComparer.Ordinal).Count() == names.Fields.Count
                    && names.Fields.All(name => resource.Fields.Any(field => field.Name == name)),
                _ => false
            };
            if (!valid) Fail(resource.Id, path, ResourceValidationCode.InvalidAnnotation, "Annotation value does not match its declared shape or field references.");
        }
    }

    internal static void Relations(ResourceDefinition source, IReadOnlyDictionary<string, ResourceDefinition> resources)
    {
        foreach (var relation in source.Relations)
        {
            string path = "relations." + relation.Name;
            if (!resources.TryGetValue(relation.TargetResource, out var target))
                Fail(source.Id, path, ResourceValidationCode.InvalidRelation, "Unknown target resource.");
            var localField = source.Fields.SingleOrDefault(field => field.Name == relation.LocalField);
            var targetField = target.Fields.SingleOrDefault(field => field.Name == relation.TargetField);
            if (localField is null || targetField is null || localField.Type != targetField.Type
                || (localField.Type == ScalarType.Enum && !localField.EnumValues.Order(StringComparer.Ordinal).SequenceEqual(targetField.EnumValues.Order(StringComparer.Ordinal)))
                || (relation.Required && localField.Nullable)
                || (relation.DisplayField is not null && !target.Fields.Any(field => field.Name == relation.DisplayField)))
                Fail(source.Id, path, ResourceValidationCode.InvalidRelation, "Unknown/incompatible fields, display field, or nullable required relation.");
        }
    }
}
