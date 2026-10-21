using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Security;
using Alxarafe.Security.AspNetCore;
using Microsoft.AspNetCore.Authorization;

namespace Alxarafe.Host;

public static class CapabilityEndpoints
{
    public const string Path = "/api/core/capabilities";
    public const string NotFound = "capability_not_found";
    public const string ExecutionFailed = "capability_execution_failed";
    private const string InputField = "input";
    private const string ReadEffect = "read";

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

    private static async Task<IResult> InvokeAsync(string capabilityId, HttpContext context, CapabilityRegistry registry,
        ModuleStateService states, IAuthorizationService authorization, IEnumerable<ICapabilityExecutor> executors)
    {
        if (!CapabilityRegistry.IsValidId(capabilityId))
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, ModuleEndpoints.InvalidRequest);
        var available = await EffectiveAsync(context, registry, states, authorization);
        var capability = available.Find(item => string.Equals(item.Id, capabilityId, StringComparison.Ordinal));
        if (capability is null || capability.Effect != ReadEffect) return UserEndpoints.Error(StatusCodes.Status404NotFound, NotFound);
        var executor = executors.FirstOrDefault(candidate => candidate.CanExecute(capability.Id));
        if (executor is null) return UserEndpoints.Error(StatusCodes.Status404NotFound, NotFound);

        context.Items[CapabilityInvocationAuditMiddleware.AuditContextItemKey] =
            new CapabilityInvocationAuditContext(capability.Id, capability.Module, Subject(context));
        var input = await ReadInputAsync(context);
        if (input is null) return UserEndpoints.Error(StatusCodes.Status400BadRequest, ModuleEndpoints.InvalidRequest);

        string invocationId = context.Items[CapabilityInvocationAuditMiddleware.InvocationIdItemKey] as string
            ?? Guid.NewGuid().ToString("N");
        var execution = new CapabilityExecutionContext(invocationId, context.TraceIdentifier, Subject(context),
            capability.Id, capability.Module, EffectivePermissions(context));
        try
        {
            var output = await executor.ExecuteAsync(capability, input, execution, context.RequestAborted);
            var normalized = CapabilityInvocationOutput.Normalize(capability.OutputSchema.Ref, output);
            if (normalized is null) return UserEndpoints.Error(StatusCodes.Status500InternalServerError, ExecutionFailed);
            return Results.Json(new CapabilityInvocationResponse(invocationId, capability.Id, normalized.Value));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception) { return UserEndpoints.Error(StatusCodes.Status500InternalServerError, ExecutionFailed); }
    }

    private static async Task<CapabilityListInput?> ReadInputAsync(HttpContext context)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var root = document.RootElement;
            var properties = root.ValueKind == JsonValueKind.Object ? root.EnumerateObject().ToArray() : [];
            if (properties.Length != 1 || properties[0].Name != InputField || properties[0].Value.ValueKind != JsonValueKind.Object)
                return null;
            string? offsetText = null;
            string? limitText = null;
            foreach (var property in properties[0].Value.EnumerateObject())
            {
                if (property.Name == CollectionPagination.OffsetParameter && offsetText is null)
                {
                    if (!IntegerText(property.Value, out offsetText)) return null;
                }
                else if (property.Name == CollectionPagination.LimitParameter && limitText is null)
                {
                    if (!IntegerText(property.Value, out limitText)) return null;
                }
                else return null;
            }
            var pagination = CollectionPagination.Parse(offsetText, limitText);
            return pagination is null ? null : new CapabilityListInput(pagination.Offset, pagination.Limit);
        }
        catch (JsonException) { return null; }
        catch (BadHttpRequestException) { return null; }
    }

    private static bool IntegerText(JsonElement value, out string text)
    {
        text = "";
        if (value.ValueKind != JsonValueKind.Number) return false;
        text = value.GetRawText();
        return text.Length > 0 && text.All(character => character is >= '0' and <= '9');
    }

    private static string? Subject(HttpContext context) =>
        context.Items[typeof(CoreUserState)] is CoreUserState state ? state.Id.ToString() : null;

    private static string[] EffectivePermissions(HttpContext context) =>
        context.User.FindAll(PermissionClaim.Type).Select(claim => claim.Value).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();

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
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CapabilityInvocationRequest([property: Required] CapabilityInvocationInput Input);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CapabilityInvocationInput(BigInteger? Offset, int? Limit);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CapabilityInvocationResponse([property: Required, MinLength(1)] string InvocationId,
    [property: Required] string CapabilityId, [property: Required] JsonElement Output);
