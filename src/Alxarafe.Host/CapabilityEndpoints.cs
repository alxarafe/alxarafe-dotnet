using System.Numerics;
using System.Text.Json.Serialization;
using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Security.AspNetCore;
using Microsoft.AspNetCore.Authorization;

namespace Alxarafe.Host;

public static class CapabilityEndpoints
{
    public const string Path = "/api/core/capabilities";
    private const string NotFound = "capability_not_found";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).RequireAuthorization().WithSharedContractErrors().WithTags("Capabilities");
        group.MapGet("", ListAsync).Produces<CapabilityPage>().Produces<UserError>(StatusCodes.Status400BadRequest).Produces<UserError>(StatusCodes.Status401Unauthorized);
        group.MapGet("/{capabilityId}", DetailAsync).Produces<CapabilityDescriptor>().Produces<UserError>(StatusCodes.Status400BadRequest)
            .Produces<UserError>(StatusCodes.Status401Unauthorized).Produces<UserError>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(HttpContext context, CapabilityRegistry registry, ModuleStateService states, IAuthorizationService authorization)
    {
        if (context.Request.Query.Any(entry => entry.Value.Count != 1 || entry.Key is not (CollectionPagination.OffsetParameter or CollectionPagination.LimitParameter)))
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, ModuleEndpoints.InvalidRequest);
        var page = CollectionPagination.Read(context.Request.Query);
        if (page is null) return UserEndpoints.Error(StatusCodes.Status400BadRequest, ModuleEndpoints.InvalidRequest);
        var available = await EffectiveAsync(context, registry, states, authorization);
        CapabilityDescriptor[] items = page.Offset >= available.Count ? [] : available.Skip((int)page.Offset).Take(page.Limit).ToArray();
        return Results.Json(new CapabilityPage(items, page.Offset, page.Limit, available.Count, CollectionPagination.IdAscending));
    }

    private static async Task<IResult> DetailAsync(string capabilityId, HttpContext context, CapabilityRegistry registry, ModuleStateService states, IAuthorizationService authorization)
    {
        if (context.Request.Query.Count != 0 || !CapabilityRegistry.IsValidId(capabilityId))
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, ModuleEndpoints.InvalidRequest);
        var available = await EffectiveAsync(context, registry, states, authorization);
        var capability = available.Find(item => string.Equals(item.Id, capabilityId, StringComparison.Ordinal));
        return capability is null ? UserEndpoints.Error(StatusCodes.Status404NotFound, NotFound) : Results.Json(capability);
    }

    private static async Task<List<CapabilityDescriptor>> EffectiveAsync(HttpContext context, CapabilityRegistry registry, ModuleStateService states, IAuthorizationService authorization)
    {
        var result = new List<CapabilityDescriptor>();
        foreach (var capability in registry.Capabilities)
        {
            if (!await states.IsEnabledAsync(capability.Module, context.RequestAborted)) continue;
            bool allowed = true;
            foreach (var permission in capability.RequiredPermissions)
                if (!(await authorization.AuthorizeAsync(context.User, permission)).Succeeded) { allowed = false; break; }
            if (allowed) result.Add(capability);
        }
        return result;
    }
}

public sealed record CapabilityPage(CapabilityDescriptor[] Items, [property: JsonConverter(typeof(OffsetJsonConverter))] BigInteger Offset,
    int Limit, long Total, IReadOnlyList<CollectionOrder> Order);
