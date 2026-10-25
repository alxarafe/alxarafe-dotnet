using System.Globalization;
using System.Net.Http;
using System.Net.Mime;
using System.Text.Json.Nodes;
using Alxarafe.AspNetCore;
using Alxarafe.Resources.Runtime;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Alxarafe.Resources.AspNetCore;

// Static structural documentation. Never consults repository instances or user claims.
public sealed partial class ResourceOpenApiTransformer(ResourceRegistry resources) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Paths ??= new OpenApiPaths();
        foreach (var resource in resources.All)
        {
            Actions(document, resource);
            string collection = ResourceEndpoints.ResourcePath + "/" + Uri.EscapeDataString(resource.Id);
            foreach (var operation in resource.Operations)
            {
                bool item = operation is ResourceOperation.Get or ResourceOperation.Update or ResourceOperation.Delete;
                string path = collection + (item ? "/{id}" : "");
                OpenApiPathItem entry;
                if (document.Paths.TryGetValue(path, out var existing)) entry = (OpenApiPathItem)existing;
                else document.Paths[path] = entry = new OpenApiPathItem();
                var projection = new OpenApiOperation
                {
                    OperationId = resource.Id + "." + Enum.GetName(operation)!.ToLowerInvariant(),
                    Description = "Implementation-specific Resource API derived from Resource Metadata. Runtime authorization and tenant/module guards apply.",
                    Parameters = [],
                    Responses = new OpenApiResponses(),
                    Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }]
                };
                if (item) projection.Parameters.Add(new OpenApiParameter
                {
                    Name = "id",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = Field(resource.Fields.Single(field => field.Name == resource.IdDefinition.Field))
                });
                if (operation == ResourceOperation.List) Query(projection);
                if (operation is ResourceOperation.Create or ResourceOperation.Update)
                    projection.RequestBody = new OpenApiRequestBody
                    {
                        Required = true,
                        Content = new Dictionary<string, OpenApiMediaType> { [MediaTypeNames.Application.Json] = new() { Schema = Record(resource, operation) } }
                    };
                string success = operation == ResourceOperation.Create ? "201" : operation == ResourceOperation.Delete ? "204" : "200";
                projection.Responses.Add(success, new OpenApiResponse
                {
                    Description = "Successful resource operation.",
                    Content = operation == ResourceOperation.Delete ? null : new Dictionary<string, OpenApiMediaType>
                    { [MediaTypeNames.Application.Json] = new() { Schema = operation == ResourceOperation.List ? Page(resource) : Record(resource, null) } }
                });
                foreach (string error in new[] { "400", "401", "403", "404", "405", "500", "503" })
                    projection.Responses.Add(error, new OpenApiResponse
                    {
                        Description = "Existing ContractError envelope; runtime/transport failure.",
                        Content = new Dictionary<string, OpenApiMediaType> { [MediaTypeNames.Application.Json] = new() { Schema = Error() } }
                    });
                entry.Operations ??= [];
                entry.Operations.Add(operation switch
                {
                    ResourceOperation.List or ResourceOperation.Get => HttpMethod.Get,
                    ResourceOperation.Create => HttpMethod.Post,
                    ResourceOperation.Update => HttpMethod.Patch,
                    _ => HttpMethod.Delete
                }, projection);
            }
        }
        return Task.CompletedTask;
    }

    public static OpenApiSchema Field(FieldDefinition field)
    {
        var schema = new OpenApiSchema
        {
            Type = field.Type switch
            {
                ScalarType.Boolean => JsonSchemaType.Boolean,
                ScalarType.Int8 or ScalarType.UInt8 or ScalarType.Int16 or ScalarType.UInt16 or ScalarType.Int32 or ScalarType.UInt32 or ScalarType.Int64 => JsonSchemaType.Integer,
                ScalarType.Decimal => JsonSchemaType.Number,
                _ => JsonSchemaType.String
            },
            Format = field.Type switch { ScalarType.Uuid => "uuid", ScalarType.Date => "date", ScalarType.Time => "time", ScalarType.DateTime => "date-time", ScalarType.Binary => "byte", _ => null },
            ReadOnly = field.ReadOnly
        };
        if (field.Nullable) schema.Type |= JsonSchemaType.Null;
        var numeric = ResourceValueValidator.NumericConstraints(field);
        if (numeric is not null)
        {
            if (numeric.ExclusiveMinimum) schema.ExclusiveMinimum = numeric.Minimum?.CanonicalText; else schema.Minimum = numeric.Minimum?.CanonicalText;
            if (numeric.ExclusiveMaximum) schema.ExclusiveMaximum = numeric.Maximum?.CanonicalText; else schema.Maximum = numeric.Maximum?.CanonicalText;
            if (numeric.Step is { } step)
            {
                if (decimal.TryParse(step.CanonicalText, NumberStyles.Number, CultureInfo.InvariantCulture, out var exact)
                    && exact > 0 && ResourceNumber.Parse(exact.ToString(CultureInfo.InvariantCulture)) == step) schema.MultipleOf = exact;
                else
                {
                    // Native OpenAPI's multipleOf uses CLR decimal. Preserve exact
                    // metadata rather than rounding an arbitrary-precision step.
                    schema.Extensions ??= new Dictionary<string, IOpenApiExtension>();
                    schema.Extensions["x-erbas-exact-multiple-of"] = new JsonNodeExtension(JsonValue.Create(step.CanonicalText));
                }
            }
        }
        if (field.Constraints is TextConstraints text) { schema.MinLength = text.MinimumLength; schema.MaxLength = text.MaximumLength; schema.Pattern = text.Pattern; }
        if (field.Type == ScalarType.Enum) schema.Enum = field.EnumValues.Order(StringComparer.Ordinal).Select(value => (JsonNode)JsonValue.Create(value)!).ToList();
        if (field.DefaultValue is { } value) schema.Default = JsonNode.Parse(ResourceTransport.Project(value).GetRawText());
        return schema;
    }
    private static OpenApiSchema Record(ResourceDefinition resource, ResourceOperation? mutation)
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            AdditionalPropertiesAllowed = false,
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal),
            Required = new HashSet<string>(StringComparer.Ordinal)
        };
        foreach (var field in resource.Fields.OrderBy(field => field.Name, StringComparer.Ordinal))
        {
            if (mutation.HasValue && (field.ReadOnly || (mutation == ResourceOperation.Update && (field.Immutable || field.Name == resource.IdDefinition.Field)))) continue;
            schema.Properties[field.Name] = Field(field);
            if (mutation is null ? field.Required || field.Name == resource.IdDefinition.Field
                : mutation == ResourceOperation.Create && field.Required && field.DefaultValue is null) schema.Required.Add(field.Name);
        }
        return schema;
    }
    private static OpenApiSchema Page(ResourceDefinition resource) => new()
    {
        Type = JsonSchemaType.Object,
        AdditionalPropertiesAllowed = false,
        Required = new HashSet<string>(["items", "offset", "limit"], StringComparer.Ordinal),
        Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["items"] = new OpenApiSchema { Type = JsonSchemaType.Array, Items = Record(resource, null) },
            ["offset"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "0" },
            ["limit"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1", Maximum = ResourceQuery.MaximumLimit.ToString(CultureInfo.InvariantCulture) },
            ["total"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "0" }
        }
    };
    private static OpenApiSchema Error() => new()
    {
        Type = JsonSchemaType.Object,
        AdditionalPropertiesAllowed = false,
        Required = new HashSet<string>(["code", "errors"], StringComparer.Ordinal),
        Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["code"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["errors"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["code"] = new OpenApiSchema { Type = JsonSchemaType.String },
                        ["resource"] = new OpenApiSchema { Type = JsonSchemaType.String },
                        ["resourceId"] = new OpenApiSchema { Type = JsonSchemaType.String },
                        ["path"] = new OpenApiSchema { Type = JsonSchemaType.String }
                    },
                    Required = new HashSet<string>(["code"], StringComparer.Ordinal),
                    AdditionalPropertiesAllowed = false
                }
            }
        }
    };
    private static void Query(OpenApiOperation operation)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter { Name = "offset", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "0", Default = JsonValue.Create(0) } });
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "limit",
            In = ParameterLocation.Query,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Minimum = "1",
                Maximum = ResourceQuery.MaximumLimit.ToString(CultureInfo.InvariantCulture),
                Default = JsonValue.Create(ResourceQuery.DefaultLimit)
            }
        });
        operation.Parameters.Add(new OpenApiParameter { Name = "search", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = ResourceQuery.MaximumSearchLength } });
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "sort",
            In = ParameterLocation.Query,
            Style = ParameterStyle.Form,
            Explode = true,
            Description = "Repeated sortable field names; prefix '-' for descending; caller order is preserved.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String } }
        });
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "filter",
            In = ParameterLocation.Query,
            Style = ParameterStyle.Form,
            Explode = true,
            Description = "Repeated URL-encoded JSON objects with exactly field, operator and value. Operators: eq, ne, lt, lte, gt, gte, contains, startsWith. Values use the field's JSON semantic type.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String } }
        });
    }
}
