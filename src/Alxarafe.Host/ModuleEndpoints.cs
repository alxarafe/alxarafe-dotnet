using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Alxarafe.Modularity;
using Microsoft.OpenApi;

namespace Alxarafe.Host;

public static class ModuleEndpoints
{
    public const string Path = "/api/modules";
    public const string EnabledField = "enabled";
    public const string InvalidRequest = "invalid_request";
    public const string NotFound = "module_not_found";
    public const string DependencyConflict = "module_dependency_conflict";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // CORE group: deliberately outside MapAlxarafeModuleEndpoints and its gate.
        var group = endpoints.MapGroup(Path).RequireAuthorization(SharedCoreEndpoint.AdminPolicy).WithTags("Modules");
        group.MapGet("", ListAsync).Produces<ModulePage>().Produces<UserError>(StatusCodes.Status400BadRequest)
            .Produces<UserError>(StatusCodes.Status401Unauthorized).Produces<UserError>(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                operation.Parameters =
                [
                    new OpenApiParameter { Name = CollectionPagination.OffsetParameter, In = ParameterLocation.Query,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "0", Default = JsonValue.Create(0) } },
                    new OpenApiParameter { Name = CollectionPagination.LimitParameter, In = ParameterLocation.Query,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer,
                            Minimum = CollectionPagination.MinimumLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            Maximum = CollectionPagination.MaximumLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            Default = JsonValue.Create(CollectionPagination.DefaultLimit) } }
                ];
                return Task.CompletedTask;
            });
        group.MapPatch("/{id}", UpdateAsync).Accepts<SetModuleEnabledRequest>(MediaTypeNames.Application.Json)
            .Produces<ModuleResponse>().Produces<UserError>(StatusCodes.Status400BadRequest)
            .Produces<UserError>(StatusCodes.Status401Unauthorized).Produces<UserError>(StatusCodes.Status403Forbidden)
            .Produces<UserError>(StatusCodes.Status404NotFound).Produces<UserError>(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListAsync(HttpContext context, ModuleRegistry registry, ModuleStateService states)
    {
        var pagination = CollectionPagination.Read(context.Request.Query);
        if (pagination is null) return UserEndpoints.Error(StatusCodes.Status400BadRequest, InvalidRequest);
        var available = registry.Modules; // Registry supplies ordinal ID order before slicing.
        IEnumerable<IAlxarafeModule> selected = pagination.Offset >= available.Count ? [] : available.Skip((int)pagination.Offset).Take(pagination.Limit);
        var items = new List<ModuleResponse>();
        foreach (var module in selected) items.Add(await RepresentationAsync(module.Descriptor, states, context.RequestAborted));
        return Results.Json(new ModulePage(items.ToArray(), pagination.Offset, pagination.Limit, available.Count, CollectionPagination.IdAscending));
    }

    private static async Task<IResult> UpdateAsync(string id, HttpContext context, ModuleRegistry registry, ModuleStateService states)
    {
        bool enabled;
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1
                || !root.TryGetProperty(EnabledField, out var property) || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return UserEndpoints.Error(StatusCodes.Status400BadRequest, InvalidRequest);
            enabled = property.GetBoolean();
        }
        catch (JsonException) { return UserEndpoints.Error(StatusCodes.Status400BadRequest, InvalidRequest); }
        catch (BadHttpRequestException) { return UserEndpoints.Error(StatusCodes.Status400BadRequest, InvalidRequest); }

        var descriptor = registry.Modules.Select(module => module.Descriptor)
            .SingleOrDefault(module => string.Equals(module.Id, id, StringComparison.Ordinal));
        if (descriptor is null) return UserEndpoints.Error(StatusCodes.Status404NotFound, NotFound);
        try
        {
            if (enabled) await states.EnableAsync(id, context.RequestAborted);
            else await states.DisableAsync(id, context.RequestAborted);
            return Results.Json(await RepresentationAsync(descriptor, states, context.RequestAborted));
        }
        catch (ModuleUnavailableException) { return UserEndpoints.Error(StatusCodes.Status404NotFound, NotFound); }
        catch (ModuleDependencyConflictException) { return UserEndpoints.Error(StatusCodes.Status409Conflict, DependencyConflict); }
    }

    private static async Task<ModuleResponse> RepresentationAsync(ModuleDescriptor descriptor, ModuleStateService states, CancellationToken cancellationToken) =>
        new(descriptor.Id, descriptor.ModuleApiVersion, descriptor.Dependencies.Order(StringComparer.Ordinal).ToArray(),
            await states.IsEnabledAsync(descriptor.Id, cancellationToken));
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModuleResponse([property: Required, MinLength(1)] string Id, int ModuleApiVersion, string[] Dependencies, bool Enabled);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetModuleEnabledRequest(bool Enabled);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModulePage(ModuleResponse[] Items, [property: JsonConverter(typeof(OffsetJsonConverter))] BigInteger Offset,
    int Limit, long Total, IReadOnlyList<CollectionOrder> Order);
