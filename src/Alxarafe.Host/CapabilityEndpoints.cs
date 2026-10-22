using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Security;
using Alxarafe.Security.AspNetCore;

namespace Alxarafe.Host;

public static class CapabilityEndpoints
{
    public const string Path = "/api/core/capabilities";
    public const string NotFound = "capability_not_found";
    public const string ExecutionFailed = "capability_execution_failed";
    private const string InputField = "input";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).RequireAuthorization().WithSharedContractErrors().WithTags("Capabilities");
        group.MapGet("", ListAsync).Produces<CapabilityPage>().Produces<UserError>(StatusCodes.Status400BadRequest).Produces<UserError>(StatusCodes.Status401Unauthorized);
        group.MapGet("/{capabilityId}", DetailAsync).Produces<CapabilityDescriptor>().Produces<UserError>(StatusCodes.Status400BadRequest)
            .Produces<UserError>(StatusCodes.Status401Unauthorized).Produces<UserError>(StatusCodes.Status404NotFound);
        group.MapPost("/{capabilityId}/invocations", InvokeAsync).Accepts<CapabilityInvocationRequest>(MediaTypeNames.Application.Json)
            .Produces<CapabilityInvocationResponse>(StatusCodes.Status200OK).Produces<UserError>(StatusCodes.Status400BadRequest)
            .Produces<UserError>(StatusCodes.Status401Unauthorized).Produces<UserError>(StatusCodes.Status404NotFound)
            .Produces<UserError>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new CapabilityInvocationMetadata());
    }

    private static async Task<IResult> ListAsync(HttpContext context, ICapabilityInvocationService service)
    {
        if (context.Request.Query.Any(entry => entry.Value.Count != 1 || entry.Key is not (CollectionPagination.OffsetParameter or CollectionPagination.LimitParameter)))
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, ModuleEndpoints.InvalidRequest);
        var page = CollectionPagination.Read(context.Request.Query);
        if (page is null) return UserEndpoints.Error(StatusCodes.Status400BadRequest, ModuleEndpoints.InvalidRequest);
        var available = await service.EffectiveAsync(context.User, context.RequestAborted);
        CapabilityDescriptor[] items = page.Offset >= available.Count ? [] : available.Skip((int)page.Offset).Take(page.Limit).ToArray();
        return Results.Json(new CapabilityPage(items, page.Offset, page.Limit, available.Count, CollectionPagination.IdAscending));
    }

    private static async Task<IResult> DetailAsync(string capabilityId, HttpContext context, ICapabilityInvocationService service)
    {
        if (context.Request.Query.Count != 0 || !CapabilityRegistry.IsValidId(capabilityId))
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, ModuleEndpoints.InvalidRequest);
        var available = await service.EffectiveAsync(context.User, context.RequestAborted);
        var capability = available.FirstOrDefault(item => string.Equals(item.Id, capabilityId, StringComparison.Ordinal));
        return capability is null ? UserEndpoints.Error(StatusCodes.Status404NotFound, NotFound) : Results.Json(capability);
    }

    private static async Task<IResult> InvokeAsync(string capabilityId, HttpContext context, ICapabilityInvocationService service)
    {
        var result = await service.InvokeAsync(new CapabilityInvocationCommand(capabilityId,
            _ => ReadInputAsync(context), context.User, Subject(context), context.TraceIdentifier,
            context.Items[CapabilityInvocationAuditMiddleware.InvocationIdItemKey] as string), context.RequestAborted);
        context.Items[CapabilityInvocationAuditMiddleware.SuppressItemKey] = true;
        return result.Outcome switch
        {
            CapabilityInvocationOutcome.Succeeded => Results.Json(new CapabilityInvocationResponse(
                result.InvocationId!, capabilityId, result.Output!.Value)),
            CapabilityInvocationOutcome.InvalidRequest => UserEndpoints.Error(StatusCodes.Status400BadRequest, ModuleEndpoints.InvalidRequest),
            CapabilityInvocationOutcome.NotFound => UserEndpoints.Error(StatusCodes.Status404NotFound, NotFound),
            _ => UserEndpoints.Error(StatusCodes.Status500InternalServerError, ExecutionFailed)
        };
    }

    private static async Task<JsonElement?> ReadInputAsync(HttpContext context)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var root = document.RootElement;
            var properties = root.ValueKind == JsonValueKind.Object ? root.EnumerateObject().ToArray() : [];
            if (properties.Length != 1 || properties[0].Name != InputField || properties[0].Value.ValueKind != JsonValueKind.Object)
                return null;
            return properties[0].Value.Clone();
        }
        catch (JsonException) { return null; }
        catch (BadHttpRequestException) { return null; }
    }

    private static string? Subject(HttpContext context) =>
        context.Items[typeof(CoreUserState)] is CoreUserState state ? state.Id.ToString() : null;
}

public sealed record CapabilityPage(CapabilityDescriptor[] Items, [property: JsonConverter(typeof(OffsetJsonConverter))] BigInteger Offset,
    int Limit, long Total, IReadOnlyList<CollectionOrder> Order);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CapabilityInvocationRequest([property: Required] CapabilityInvocationInput Input);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CapabilityInvocationInput(BigInteger? Offset, int? Limit);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CapabilityInvocationResponse([property: Required, MinLength(1)] string InvocationId,
    [property: Required] string CapabilityId, [property: Required] JsonElement Output);
