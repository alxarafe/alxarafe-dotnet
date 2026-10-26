using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alxarafe.Modularity;
using Alxarafe.Resources.Runtime;

namespace Alxarafe.Resources.AspNetCore;

public sealed record ResourceOperationDto(bool Declared, bool Authorized);
public sealed record ResourceIdDto(string Field);
public sealed record ResourceCapabilitiesDto(bool Filterable, bool Sortable, bool Searchable, IReadOnlyList<string> FilterOperators);
public sealed record ResourceAnnotationDto(string Vocabulary, string Term, JsonElement Value);
public sealed record ResourceConstraintsDto(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Minimum = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Maximum = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ExclusiveMinimum = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ExclusiveMaximum = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? MultipleOf = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Precision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Scale = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MinimumLength = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaximumLength = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Pattern = null);
public sealed record ResourceFieldDto(string Name, string Type, bool Nullable, bool Required, bool ReadOnly, bool Immutable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Default,
    ResourceConstraintsDto Constraints, ResourceCapabilitiesDto Capabilities, IReadOnlyList<string> EnumValues, IReadOnlyList<ResourceAnnotationDto> Annotations);
public sealed record ResourceRelationDto(string Name, string Kind, string TargetResource, string LocalField, string TargetField, string? DisplayField, bool Required);
public sealed record ResourceActionParameterDto(string Name, string Type, bool Nullable, bool Required,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Default,
    ResourceConstraintsDto Constraints, IReadOnlyList<string> EnumValues, IReadOnlyList<ResourceAnnotationDto> Annotations);
public sealed record ResourceActionDto(string Name, string Scope, bool RequiresSelection, bool Authorized, IReadOnlyList<ResourceAnnotationDto> Annotations, IReadOnlyList<ResourceActionParameterDto> Parameters);
public sealed record ResourceMetadataDto(string Id, string Version, string ModuleId, string DefinitionHash,
    ResourceIdDto IdDefinition, bool ModuleEnabled, IReadOnlyList<ResourceFieldDto> Fields, IReadOnlyList<ResourceRelationDto> Relations,
    IReadOnlyDictionary<string, ResourceOperationDto> Operations, IReadOnlyList<ResourceActionDto> Actions, IReadOnlyList<ResourceAnnotationDto> Annotations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ResourcePresentationDto? Presentation = null);
public sealed record ResourceSummaryDto(string Id, string Version, string ModuleId, string DefinitionHash,
    IReadOnlyDictionary<string, ResourceOperationDto> Operations, string MetadataPath);

public sealed partial class ResourceMetadataProjection(ResourceRegistry resources, ModuleStateService modules, IResourceAuthorization authorization)
{
    public async Task<ResourceMetadataDto> ProjectAsync(ResourceDefinition resource, CancellationToken ct = default)
    {
        bool enabled = await modules.IsEnabledAsync(resource.ModuleId, ct);
        var operations = new Dictionary<string, ResourceOperationDto>(StringComparer.Ordinal);
        foreach (var operation in Enum.GetValues<ResourceOperation>())
        {
            bool declared = resource.Operations.Contains(operation);
            bool authorized = declared && await authorization.IsAllowedAsync(resource.Security.Rules.Single(rule => rule.Operation == operation).Permissions, ct);
            operations.Add(Enum.GetName(operation)!.ToLowerInvariant(), new(declared, authorized));
        }
        var actions = new List<ResourceActionDto>();
        foreach (var action in resource.Actions.OrderBy(action => action.Name, StringComparer.Ordinal))
            actions.Add(new(action.Name, Enum.GetName(action.Scope)!.ToUpperInvariant(), action.RequiresSelection,
                await authorization.IsAllowedAsync([action.Permission], ct), Annotations(action.Annotations), action.Parameters.OrderBy(parameter => parameter.Name, StringComparer.Ordinal).Select(Parameter).ToArray()));
        return new(resource.Id, resource.Version, resource.ModuleId, resources.DefinitionHashes[resource.Id], new(resource.IdDefinition.Field), enabled,
            resource.Fields.OrderBy(field => field.Name, StringComparer.Ordinal).Select(Field).ToArray(),
            resource.Relations.OrderBy(relation => relation.Name, StringComparer.Ordinal).Select(relation => new ResourceRelationDto(relation.Name,
                Enum.GetName(relation.Kind) == nameof(RelationKind.ManyToOne) ? "MANY_TO_ONE" : throw new InvalidOperationException("Unsupported relation projection."),
                relation.TargetResource, relation.LocalField, relation.TargetField, relation.DisplayField, relation.Required)).ToArray(),
            operations, actions, Annotations(resource.Annotations), Presentation(resource.Presentation));
    }
    internal static FieldDefinition ParameterField(ActionParameterDefinition parameter) => new(parameter.Name, parameter.Type,
        parameter.Nullable, parameter.Required, defaultValue: parameter.DefaultValue, constraints: parameter.Constraints,
        enumValues: parameter.EnumValues, annotations: parameter.Annotations);
    private static ResourceActionParameterDto Parameter(ActionParameterDefinition parameter) => new(parameter.Name, Enum.GetName(parameter.Type)!.ToUpperInvariant(),
        parameter.Nullable, parameter.Required, parameter.DefaultValue is null ? null : ResourceTransport.Project(parameter.DefaultValue),
        Constraints(ParameterField(parameter)), parameter.EnumValues.Order(StringComparer.Ordinal).ToArray(), Annotations(parameter.Annotations));
    public static string ETag(ResourceMetadataDto metadata)
    {
        // Authorization/module state is not part of structural DefinitionHash. It must
        // participate in HTTP coherence so a permission change cannot produce stale 304.
        byte[] representation = JsonSerializer.SerializeToUtf8Bytes(metadata, ResourceTransport.Json);
        return '"' + metadata.DefinitionHash + "." + Convert.ToHexStringLower(SHA256.HashData(representation)) + '"';
    }
    private static ResourceFieldDto Field(FieldDefinition field) => new(field.Name, Enum.GetName(field.Type)!.ToUpperInvariant(),
        field.Nullable, field.Required, field.ReadOnly, field.Immutable,
        field.DefaultValue is null ? null : ResourceTransport.Project(field.DefaultValue), Constraints(field),
        new(field.Capabilities.Filterable, field.Capabilities.Sortable, field.Capabilities.Searchable, ResourceQuery.AllowedOperators(field).Select(ResourceTransport.OperatorName).ToArray()),
        field.EnumValues.Order(StringComparer.Ordinal).ToArray(), Annotations(field.Annotations));
    public static ResourceConstraintsDto Constraints(FieldDefinition field)
    {
        var numeric = ResourceValueValidator.NumericConstraints(field);
        if (numeric is not null) return new(Number(numeric.Minimum), Number(numeric.Maximum), numeric.ExclusiveMinimum,
            numeric.ExclusiveMaximum, Number(numeric.Step), numeric.Precision, numeric.Scale);
        return field.Constraints is TextConstraints text ? new(MinimumLength: text.MinimumLength ?? 0, MaximumLength: text.MaximumLength, Pattern: text.Pattern) : new();
    }
    private static JsonElement? Number(ResourceNumber? value) => value is null ? null : ResourceTransport.Project(new NumericValue(value));
    private static ResourceAnnotationDto[] Annotations(IEnumerable<Annotation> annotations) => annotations.OrderBy(annotation => annotation.Key, StringComparer.Ordinal)
        .Select(annotation => new ResourceAnnotationDto(annotation.Vocabulary, annotation.Term, annotation.Value switch
        {
            AnnotationText text => JsonSerializer.SerializeToElement(text.Value),
            AnnotationBoolean boolean => JsonSerializer.SerializeToElement(boolean.Value),
            AnnotationNumber number => ResourceTransport.Project(new NumericValue(number.Value)),
            AnnotationFieldNames names => JsonSerializer.SerializeToElement(names.Fields),
            _ => throw new InvalidOperationException("Unsupported annotation projection.")
        })).ToArray();
}
