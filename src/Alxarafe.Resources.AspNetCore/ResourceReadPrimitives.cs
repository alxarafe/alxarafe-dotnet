using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Resources.Runtime;
using Microsoft.OpenApi;

namespace Alxarafe.Resources.AspNetCore;

// Modules explicitly contribute these through their existing IReadPrimitiveProvider.
// Merely registering a ResourceDefinition never opts it into AI discovery.
public static class ResourceReadPrimitives
{
    public const string Filters = "filters";
    private const string Field = "field", Operator = "operator", Value = "value";
    private const string Search = "search";
    public static string Id(string resourceId) => resourceId + ".list";
    public static string Name(string resourceId) => Id(resourceId).Replace('.', '_').Replace('-', '_');

    public static ReadPrimitiveDescriptor List(ResourceDefinition resource)
    {
        if (!resource.Operations.Contains(ResourceOperation.List)) throw new InvalidOperationException("A READ primitive requires LIST.");
        var permissions = resource.Security.Rules.Single(rule => rule.Operation == ResourceOperation.List).Permissions;
        var properties = new JsonObject
        {
            [CollectionPagination.OffsetParameter] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 },
            [CollectionPagination.LimitParameter] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = ResourceQuery.MaximumLimit, ["default"] = ResourceQuery.DefaultLimit }
        };
        var alternatives = new JsonArray();
        foreach (var field in resource.Fields.Where(field => field.Capabilities.Filterable))
        {
            var operations = ResourceQuery.AllowedOperators(field);
            alternatives.Add(Filter(field.Name, operations.Where(op => op is not (ResourceFilterOperator.Contains or ResourceFilterOperator.StartsWith)), Scalar(field)));
            var fragments = operations.Where(op => op is ResourceFilterOperator.Contains or ResourceFilterOperator.StartsWith).ToArray();
            if (fragments.Length > 0) alternatives.Add(Filter(field.Name, fragments, new JsonObject { ["type"] = "string" }));
            if (field.Nullable) alternatives.Add(Filter(field.Name, [ResourceFilterOperator.Equal, ResourceFilterOperator.NotEqual], new JsonObject { ["type"] = "null" }));
        }
        if (alternatives.Count > 0)
            properties[Filters] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["anyOf"] = alternatives }, ["description"] = "AND filters using the resource's declared fields, operators and typed values. Omit unneeded filters." };
        if (resource.Fields.Any(field => field.Capabilities.Searchable))
            properties[Search] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = ResourceQuery.MaximumSearchLength };
        var schema = Closed(properties);
        string description = "List " + resource.Id + " records. Returns items with all declared fields, offset, limit and optional total. "
            + "Follow pagination for complete sets. Resolve related identifiers from the corresponding resource lists; null is a typed missing value. "
            + "Returned fields: " + string.Join(", ", resource.Fields.Select(field => field.Name)) + ".";
        if (resource.Relations.Count > 0)
            description += " Relations: " + string.Join("; ", resource.Relations.Select(relation => relation.LocalField + " references "
                + relation.TargetResource + "." + relation.TargetField + (relation.DisplayField is null ? "" : " (display " + relation.DisplayField + ")"))) + ".";
        return new(Id(resource.Id), resource.ModuleId, Name(resource.Id), description, JsonSerializer.SerializeToElement(schema), permissions);
    }

    private static JsonObject Closed(JsonObject properties) => new() { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
    private static JsonObject Filter(string field, IEnumerable<ResourceFilterOperator> operators, JsonObject value)
    {
        var result = Closed(new JsonObject
        {
            [Field] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(JsonValue.Create(field)) },
            [Operator] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(operators.Select(op => (JsonNode?)JsonValue.Create(ResourceTransport.OperatorName(op))).ToArray()) },
            [Value] = value
        });
        result["required"] = new JsonArray(JsonValue.Create(Field), JsonValue.Create(Operator), JsonValue.Create(Value));
        return result;
    }
    private static JsonObject Scalar(FieldDefinition field)
    {
        // Reuse the existing scalar/constraint projection, but a query value is
        // neither a mutation default nor a read-only output. NULL has its own eq/ne branch.
        var input = new FieldDefinition(field.Name, field.Type, constraints: field.Constraints, enumValues: field.EnumValues);
        var schema = ResourceOpenApiTransformer.Field(input);
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        schema.SerializeAsV31(new OpenApiJsonWriter(text));
        return JsonNode.Parse(text.ToString())!.AsObject();
    }

    internal static ResourceQuery Query(ResourceDefinition definition, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new ReadPrimitiveInputException();
        long offset = 0; int limit = ResourceQuery.DefaultLimit; string? search = null;
        var filters = new List<ResourceFilter>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in input.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw new ReadPrimitiveInputException();
            switch (property.Name)
            {
                case CollectionPagination.OffsetParameter when property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out offset): break;
                case CollectionPagination.LimitParameter when property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out limit): break;
                case Search when property.Value.ValueKind == JsonValueKind.String: search = property.Value.GetString(); break;
                case Filters when property.Value.ValueKind == JsonValueKind.Array && definition.Fields.Any(field => field.Capabilities.Filterable):
                    foreach (var filter in property.Value.EnumerateArray()) filters.Add(ResourceTransport.Filter(definition, filter));
                    break;
                default: throw new ReadPrimitiveInputException();
            }
        }
        return new(offset, limit, filters, search: search);
    }
}
