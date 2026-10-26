using System.Net.Http;
using System.Net.Mime;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Alxarafe.Resources.AspNetCore;

public sealed partial class ResourceOpenApiTransformer
{
    private static void Actions(OpenApiDocument document, ResourceDefinition resource)
    {
        if (resource.Actions.Count == 0) return;
        string root = ResourceActionEndpoints.Path + "/" + Uri.EscapeDataString(resource.Id);
        foreach (var action in resource.Actions.OrderBy(action => action.Name, StringComparer.Ordinal))
        {
            var parameters = ObjectSchema();
            foreach (var parameter in action.Parameters.OrderBy(parameter => parameter.Name, StringComparer.Ordinal))
            {
                parameters.Properties![parameter.Name] = Field(ResourceMetadataProjection.ParameterField(parameter));
                if (parameter.Required && parameter.DefaultValue is null) parameters.Required!.Add(parameter.Name);
            }
            var body = ObjectSchema();
            if (action.Scope != ActionScope.Collection) { body.Properties!["target"] = Target(resource, action.Scope); body.Required!.Add("target"); }
            body.Properties!["parameters"] = parameters;
            if (parameters.Required!.Count != 0) body.Required!.Add("parameters");
            Add(root + "/" + Uri.EscapeDataString(action.Name) + ResourceActionEndpoints.ExecutionSegment, resource.Id + ".action." + action.Name, body, null);
        }
        var request = new OpenApiSchema
        {
            OneOf = resource.Actions.Select(action => action.Scope).Distinct().Order().Select(scope =>
        {
            var target = Target(resource, scope);
            target.Properties!["scope"] = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [(JsonNode)JsonValue.Create(Enum.GetName(scope)!.ToUpperInvariant())!] };
            target.Required!.Add("scope");
            return (IOpenApiSchema)target;
        }).ToList()
        };
        var state = ObjectSchema();
        state.Properties!["name"] = new OpenApiSchema { Type = JsonSchemaType.String };
        state.Properties["authorized"] = new OpenApiSchema { Type = JsonSchemaType.Boolean };
        state.Properties["available"] = new OpenApiSchema { Type = JsonSchemaType.Boolean };
        state.Properties["unavailableReason"] = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = Runtime.ResourceActionReason.MaximumLength };
        state.Required!.UnionWith(["name", "authorized", "available"]);
        Add(root + ResourceActionEndpoints.AvailabilitySegment, resource.Id + ".action-availability", request, new OpenApiSchema { Type = JsonSchemaType.Array, Items = state });

        void Add(string path, string operationId, OpenApiSchema body, OpenApiSchema? result)
        {
            var operation = new OpenApiOperation
            {
                OperationId = operationId,
                Description = "Generic Resource Runtime candidate action projection. Authorization and contextual availability are rechecked on execution; handlers own business atomicity.",
                Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }],
                RequestBody = new OpenApiRequestBody { Required = true, Content = new Dictionary<string, OpenApiMediaType> { [MediaTypeNames.Application.Json] = new() { Schema = body } } },
                Responses = new OpenApiResponses()
            };
            operation.Responses.Add(result is null ? "204" : "200", new OpenApiResponse
            {
                Description = result is null ? "Action completed; refresh affected views." : "Target-specific action authorization and availability.",
                Content = result is null ? null : new Dictionary<string, OpenApiMediaType> { [MediaTypeNames.Application.Json] = new() { Schema = result } }
            });
            foreach (string status in new[] { "400", "401", "403", "404", "409", "500", "503" })
                operation.Responses.Add(status, new OpenApiResponse
                {
                    Description = "Existing ContractError envelope with stable codes; no exception text.",
                    Content = new Dictionary<string, OpenApiMediaType> { [MediaTypeNames.Application.Json] = new() { Schema = Error() } }
                });
            document.Paths[path] = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Post] = operation } };
        }
    }
    private static OpenApiSchema ObjectSchema() => new()
    {
        Type = JsonSchemaType.Object,
        AdditionalPropertiesAllowed = false,
        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal),
        Required = new HashSet<string>(StringComparer.Ordinal)
    };
    private static OpenApiSchema Target(ResourceDefinition resource, ActionScope scope)
    {
        var target = ObjectSchema();
        var id = Field(resource.Fields.Single(field => field.Name == resource.IdDefinition.Field));
        id.ReadOnly = false;
        if (scope == ActionScope.Entity) { target.Properties!["id"] = id; target.Required!.Add("id"); }
        if (scope == ActionScope.Selection)
        {
            target.Properties!["ids"] = new OpenApiSchema { Type = JsonSchemaType.Array, Items = id, MinItems = 1, MaxItems = Runtime.ResourceActionRuntime.MaximumSelection, UniqueItems = true };
            target.Required!.Add("ids");
        }
        return target;
    }
}
