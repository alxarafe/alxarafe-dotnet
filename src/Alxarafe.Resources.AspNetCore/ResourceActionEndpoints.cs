using System.Text.Json;
using System.Text.Json.Serialization;
using Alxarafe.Resources.Runtime;
using Alxarafe.Security.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Alxarafe.Resources.AspNetCore;

public sealed record ResourceActionStateDto(string Name, bool Authorized, bool Available,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UnavailableReason);
public static class ResourceActionEndpoints
{
    public const string Path = "/api/resource-actions";
    public const string AvailabilitySegment = "/availability";
    public const string ExecutionSegment = "/execute";
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).RequireAuthorization().WithSharedContractErrors();
        group.MapPost("/{resource}/{action}" + ExecutionSegment, (HttpContext http, string resource, string action) => Dispatch(http, resource, action)).ExcludeFromDescription();
        group.MapPost("/{resource}" + AvailabilitySegment, (HttpContext http, string resource) => Dispatch(http, resource, null)).ExcludeFromDescription();
    }
    private static async Task<IResult> Dispatch(HttpContext http, string resource, string? actionName)
    {
        try
        {
            if (http.Request.Query.Count != 0) throw new ResourceTransportException("query");
            var definition = ResourceEndpoints.Lookup(http, resource);
            ActionDefinition? action = actionName is null ? null : definition.Actions.SingleOrDefault(item => item.Name == actionName)
                ?? throw new ResourceRuntimeException(resource, ResourceRuntimeError.ActionNotFound);
            using var document = await ResourceTransport.ReadDocumentAsync(http.Request, http.RequestAborted);
            var body = document.RootElement;
            var runtime = http.RequestServices.GetRequiredService<ResourceActionRuntime>();
            if (action is null)
            {
                Object(body, ["scope", "id", "ids"]);
                if (!body.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.String) throw new ResourceTransportException("scope");
                var parsed = scope.GetString() switch { "ENTITY" => ActionScope.Entity, "COLLECTION" => ActionScope.Collection, "SELECTION" => ActionScope.Selection, _ => throw new ResourceTransportException("scope") };
                var states = await runtime.AvailabilityAsync(resource, Target(definition, parsed, body, envelope: true), http.RequestAborted);
                return ResourceEndpoints.Json(states.Select(state => new ResourceActionStateDto(state.Name, state.Authorized, state.Available, state.UnavailableReason)).ToArray());
            }
            Object(body, ["target", "parameters"]);
            var target = Target(definition, action.Scope, body.TryGetProperty("target", out var selected) ? selected : default, envelope: false);
            var values = new Dictionary<string, ScalarValue>(StringComparer.Ordinal);
            if (body.TryGetProperty("parameters", out var parameters))
            {
                Object(parameters, action.Parameters.Select(parameter => parameter.Name).ToArray());
                foreach (var property in parameters.EnumerateObject())
                {
                    var parameter = action.Parameters.Single(parameter => parameter.Name == property.Name);
                    try { values.Add(property.Name, ResourceTransport.Parse(parameter.Type, property.Value)); }
                    catch (ResourceTransportException) { throw new ResourceTransportException("parameters." + property.Name); }
                }
            }
            await runtime.ExecuteAsync(resource, action.Name, target, new(values), http.RequestAborted);
            return Results.NoContent();
        }
        catch (ResourceRuntimeException failure) { return await ResourceEndpoints.RuntimeError(http, failure); }
        catch (ResourceTransportException failure) { return ResourceEndpoints.Error(http, StatusCodes.Status400BadRequest, ResourceEndpoints.InvalidRequest, resource, failure.Path); }
        catch (JsonException) { return ResourceEndpoints.Error(http, StatusCodes.Status400BadRequest, ResourceEndpoints.InvalidRequest, resource, "body"); }
        catch (FormatException) { return ResourceEndpoints.Error(http, StatusCodes.Status400BadRequest, ResourceEndpoints.InvalidRequest, resource, "value"); }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception) { return ResourceEndpoints.Error(http, StatusCodes.Status500InternalServerError, ResourceEndpoints.ExecutionFailed); }
    }
    private static void Object(JsonElement element, IReadOnlyCollection<string> allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new ResourceTransportException("body");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw new ResourceTransportException(property.Name);
    }
    private static ResourceActionTarget Target(ResourceDefinition definition, ActionScope scope, JsonElement target, bool envelope)
    {
        if (scope == ActionScope.Collection)
        {
            if (envelope ? target.TryGetProperty("id", out _) || target.TryGetProperty("ids", out _) : target.ValueKind != JsonValueKind.Undefined)
                throw new ResourceTransportException("target");
            return new ResourceCollectionTarget();
        }
        if (!envelope) Object(target, scope == ActionScope.Entity ? ["id"] : ["ids"]);
        if (scope == ActionScope.Entity)
        {
            if (target.TryGetProperty("ids", out _) || !target.TryGetProperty("id", out var id)) throw new ResourceTransportException("target.id");
            return new ResourceEntityTarget(Id(definition, id));
        }
        if (target.TryGetProperty("id", out _) || !target.TryGetProperty("ids", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() is 0 or > ResourceActionRuntime.MaximumSelection)
            throw new ResourceTransportException("target.ids");
        return new ResourceSelectionTarget(ids.EnumerateArray().Select(id => Id(definition, id)));
    }
    private static ResourceIdentifier Id(ResourceDefinition definition, JsonElement value)
    {
        var field = definition.Fields.Single(field => field.Name == definition.IdDefinition.Field);
        return new(field.Name, ResourceTransport.Parse(field.Type, value));
    }
}
