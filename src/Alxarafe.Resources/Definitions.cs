namespace Alxarafe.Resources;

public sealed record FieldCapabilities(bool Filterable = false, bool Sortable = false, bool Searchable = false);

public sealed record FieldDefinition
{
    public string Name { get; }
    public ScalarType Type { get; }
    public bool Nullable { get; }
    public bool Required { get; }
    public bool ReadOnly { get; }
    public bool Immutable { get; }
    public ScalarValue? DefaultValue { get; }
    public FieldConstraints? Constraints { get; }
    public FieldCapabilities Capabilities { get; }
    public IReadOnlyList<string> EnumValues { get; }
    public IReadOnlyList<Annotation> Annotations { get; }

    public FieldDefinition(string name, ScalarType type, bool nullable = false, bool required = false,
        bool readOnly = false, bool immutable = false, ScalarValue? defaultValue = null,
        FieldConstraints? constraints = null, FieldCapabilities? capabilities = null,
        IEnumerable<string>? enumValues = null, IEnumerable<Annotation>? annotations = null)
    {
        Name = name; Type = type; Nullable = nullable; Required = required; ReadOnly = readOnly; Immutable = immutable;
        DefaultValue = defaultValue; Constraints = constraints; Capabilities = capabilities ?? new();
        EnumValues = Snapshot.Of(enumValues); Annotations = Snapshot.Of(annotations);
    }
}

public sealed record IdDefinition(string Field);
public enum ResourceOperation { List, Get, Create, Update, Delete }
public sealed record OperationSecurityRule
{
    public ResourceOperation Operation { get; }
    public IReadOnlyList<string> Permissions { get; }
    public OperationSecurityRule(ResourceOperation operation, IEnumerable<string> permissions)
    {
        Operation = operation; Permissions = Snapshot.Of(permissions);
    }
}
public sealed record ResourceSecurity
{
    public IReadOnlyList<OperationSecurityRule> Rules { get; }
    public ResourceSecurity(IEnumerable<OperationSecurityRule> rules) => Rules = Snapshot.Of(rules);
}

public enum RelationKind { ManyToOne }
public sealed record RelationDefinition(string Name, RelationKind Kind, string TargetResource, string LocalField,
    string TargetField, string? DisplayField = null, bool Required = false);
public enum ActionScope { Entity, Collection, Selection }
public sealed record ActionParameterDefinition
{
    internal FieldDefinition Field { get; }
    public string Name => Field.Name;
    public ScalarType Type => Field.Type;
    public bool Nullable => Field.Nullable;
    public bool Required => Field.Required;
    public ScalarValue? DefaultValue => Field.DefaultValue;
    public FieldConstraints? Constraints => Field.Constraints;
    public IReadOnlyList<string> EnumValues => Field.EnumValues;
    public IReadOnlyList<Annotation> Annotations => Field.Annotations;
    public ActionParameterDefinition(string name, ScalarType type, bool nullable = false, bool required = false,
        ScalarValue? defaultValue = null, FieldConstraints? constraints = null,
        IEnumerable<string>? enumValues = null, IEnumerable<Annotation>? annotations = null)
        => Field = new(name, type, nullable, required, defaultValue: defaultValue, constraints: constraints,
            enumValues: enumValues, annotations: annotations);
}
public sealed record ActionDefinition
{
    public IReadOnlyList<ActionParameterDefinition> Parameters { get; }
    public string Name { get; }
    public ActionScope Scope { get; }
    public string Permission { get; }
    public bool RequiresSelection { get; }
    public IReadOnlyList<Annotation> Annotations { get; }
    public ActionDefinition(string name, ActionScope scope, string permission, bool requiresSelection = false,
        IEnumerable<Annotation>? annotations = null, IEnumerable<ActionParameterDefinition>? parameters = null)
    {
        Parameters = Snapshot.Of(parameters);
        Name = name; Scope = scope; Permission = permission; RequiresSelection = requiresSelection;
        Annotations = Snapshot.Of(annotations);
    }
}

public sealed record ResourceDefinition
{
    public string Id { get; }
    public string Version { get; }
    public string ModuleId { get; }
    public IdDefinition IdDefinition { get; }
    public IReadOnlyList<FieldDefinition> Fields { get; }
    public IReadOnlyList<RelationDefinition> Relations { get; }
    public IReadOnlyList<ResourceOperation> Operations { get; }
    public ResourceSecurity Security { get; }
    public IReadOnlyList<ActionDefinition> Actions { get; }
    public IReadOnlyList<Annotation> Annotations { get; }

    public ResourceDefinition(string id, string version, string moduleId, IdDefinition idDefinition,
        IEnumerable<FieldDefinition> fields, IEnumerable<ResourceOperation> operations, ResourceSecurity security,
        IEnumerable<RelationDefinition>? relations = null, IEnumerable<ActionDefinition>? actions = null,
        IEnumerable<Annotation>? annotations = null)
    {
        Id = id; Version = version; ModuleId = moduleId; IdDefinition = idDefinition;
        Fields = Snapshot.Of(fields); Operations = Snapshot.Of(operations); Security = security;
        Relations = Snapshot.Of(relations); Actions = Snapshot.Of(actions); Annotations = Snapshot.Of(annotations);
    }
}

internal static class Snapshot
{
    internal static IReadOnlyList<T> Of<T>(IEnumerable<T>? items) => Array.AsReadOnly(items?.ToArray() ?? []);
}
