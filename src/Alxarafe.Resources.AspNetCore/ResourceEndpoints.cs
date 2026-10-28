using System.Net.Mime;
using System.Text.Json;
using Alxarafe.AspNetCore;
using Alxarafe.Core;
using Alxarafe.Resources.Runtime;
using Alxarafe.Security.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Alxarafe.Resources.AspNetCore;

public static class ResourceEndpoints
{
    public const string ResourcePath = "/api/resources";
    public const string MetadataPath = "/api/meta/resources";
    public const string PresentationSuffix = "/presentation";
    private const string MetadataCacheControl = "private, no-cache";
    public const string InvalidRequest = "invalid_request";
    public const string NotFound = "resource_not_found";
    public const string NotSupported = "resource_operation_not_supported";
    public const string Conflict = "resource_conflict";
    public const string ExecutionFailed = "resource_execution_failed";
    public const string RepositoryMissing = "resource_repository_not_registered";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var crud = endpoints.MapGroup(ResourcePath).RequireAuthorization().WithSharedContractErrors();
        crud.MapGet("/{resource}", (HttpContext http, string resource) => Execute(http, resource, ResourceOperation.List)).ExcludeFromDescription();
        crud.MapGet("/{resource}/{id}", (HttpContext http, string resource, string id) => Execute(http, resource, ResourceOperation.Get, id)).ExcludeFromDescription();
        crud.MapPost("/{resource}", (HttpContext http, string resource) => Execute(http, resource, ResourceOperation.Create)).ExcludeFromDescription();
        crud.MapPatch("/{resource}/{id}", (HttpContext http, string resource, string id) => Execute(http, resource, ResourceOperation.Update, id)).ExcludeFromDescription();
        crud.MapDelete("/{resource}/{id}", (HttpContext http, string resource, string id) => Execute(http, resource, ResourceOperation.Delete, id)).ExcludeFromDescription();
        var metadata = endpoints.MapGroup(MetadataPath).RequireAuthorization().WithSharedContractErrors().WithTags("Resource metadata implementation-specific");
        metadata.MapGet("", (Func<HttpContext, Task<IResult>>)Discover).Produces<IReadOnlyList<ResourceSummaryDto>>().Produces<ContractError>(StatusCodes.Status401Unauthorized);
        metadata.MapGet("/{resource}" + PresentationSuffix, ContextPresentation).Produces<ResourceContextPresentationDto>().Produces(StatusCodes.Status304NotModified)
            .Produces<ContractError>(StatusCodes.Status400BadRequest).Produces<ContractError>(StatusCodes.Status404NotFound).Produces<ContractError>(StatusCodes.Status401Unauthorized);
        metadata.MapGet("/{resource}", Metadata).Produces<ResourceMetadataDto>().Produces(StatusCodes.Status304NotModified)
            .Produces<ContractError>(StatusCodes.Status404NotFound).Produces<ContractError>(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> Execute(HttpContext http, string resource, ResourceOperation operation, string? id = null)
    {
        try
        {
            var definition = Lookup(http, resource);
            if (operation != ResourceOperation.List && http.Request.Query.Count != 0) throw new ResourceTransportException("query");
            var runtime = http.RequestServices.GetRequiredService<ResourceRuntime>();
            var ct = http.RequestAborted;
            switch (operation)
            {
                case ResourceOperation.List:
                    var page = await runtime.ListAsync(resource, ResourceTransport.Query(http.Request, definition), ct);
                    return Json(new ResourcePageDto(page.Items.Select(ResourceTransport.Record).ToArray(), page.Offset, page.Limit, page.Total));
                case ResourceOperation.Get:
                    return Json(ResourceTransport.Record(await runtime.GetAsync(resource, ResourceTransport.Identifier(definition, id!), ct)));
                case ResourceOperation.Create:
                    var created = await runtime.CreateAsync(resource, await ResourceTransport.ReadAsync(http.Request, definition, ct), ct);
                    http.Response.Headers.Location = ResourcePath + "/" + Uri.EscapeDataString(resource) + "/" + Uri.EscapeDataString(IdentifierText(created.Values[definition.IdDefinition.Field]));
                    return Json(ResourceTransport.Record(created), StatusCodes.Status201Created);
                case ResourceOperation.Update:
                    return Json(ResourceTransport.Record(await runtime.UpdateAsync(resource, ResourceTransport.Identifier(definition, id!),
                        await ResourceTransport.ReadAsync(http.Request, definition, ct), ct)));
                case ResourceOperation.Delete:
                    await runtime.DeleteAsync(resource, ResourceTransport.Identifier(definition, id!), ct);
                    return Results.NoContent();
                default: throw new ResourceTransportException("operation");
            }
        }
        catch (ResourceRuntimeException failure) { return await RuntimeError(http, failure); }
        catch (ResourceTransportException failure) { return Error(http, StatusCodes.Status400BadRequest, InvalidRequest, resource, failure.Path); }
        catch (JsonException) { return Error(http, StatusCodes.Status400BadRequest, InvalidRequest, resource, "body"); }
        catch (FormatException) { return Error(http, StatusCodes.Status400BadRequest, InvalidRequest, resource, "value"); }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception) { return Error(http, StatusCodes.Status500InternalServerError, ExecutionFailed); }
    }

    private static async Task<IResult> Discover(HttpContext http)
    {
        try
        {
            if (http.Request.Query.Count != 0) throw new ResourceTransportException("query");
            var projection = http.RequestServices.GetRequiredService<ResourceMetadataProjection>();
            var summaries = new List<ResourceSummaryDto>();
            foreach (var resource in http.RequestServices.GetRequiredService<ResourceRegistry>().All)
            {
                var item = await projection.ProjectAsync(resource, http.RequestAborted);
                if (item.ModuleEnabled) summaries.Add(new(item.Id, item.Version, item.ModuleId, item.DefinitionHash, item.Operations,
                    MetadataPath + "/" + Uri.EscapeDataString(item.Id)));
            }
            http.Response.Headers.CacheControl = MetadataCacheControl;
            http.Response.Headers.Vary = "Authorization";
            return Json(summaries);
        }
        catch (ResourceTransportException failure) { return Error(http, StatusCodes.Status400BadRequest, InvalidRequest, path: failure.Path); }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception) { return Error(http, StatusCodes.Status500InternalServerError, ExecutionFailed); }
    }

    private static async Task<IResult> Metadata(HttpContext http, string resource)
    {
        try
        {
            if (http.Request.Query.Count != 0) throw new ResourceTransportException("query");
            var definition = Lookup(http, resource);
            var item = await http.RequestServices.GetRequiredService<ResourceMetadataProjection>().ProjectAsync(definition, http.RequestAborted);
            string etag = ResourceMetadataProjection.ETag(item);
            http.Response.Headers.CacheControl = MetadataCacheControl;
            http.Response.Headers.Vary = "Authorization";
            http.Response.Headers.ETag = etag;
            var conditions = http.Request.GetTypedHeaders().IfNoneMatch;
            if (conditions?.Any(condition => condition.Tag.Value == "*" || condition.Tag.Value == etag) == true)
                return Results.StatusCode(StatusCodes.Status304NotModified);
            return Json(item);
        }
        catch (ResourceRuntimeException failure) { return await RuntimeError(http, failure); }
        catch (ResourceTransportException failure) { return Error(http, StatusCodes.Status400BadRequest, InvalidRequest, resource, failure.Path); }
        catch (FormatException) { return Error(http, StatusCodes.Status400BadRequest, InvalidRequest); }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception) { return Error(http, StatusCodes.Status500InternalServerError, ExecutionFailed); }
    }

    private static async Task<IResult> ContextPresentation(HttpContext http, string resource)
    {
        try
        {
            if (http.Request.Query.Count != 0) throw new ResourceTransportException("query");
            var definition = Lookup(http, resource);
            var item = http.RequestServices.GetRequiredService<ResourceMetadataProjection>().ContextPresentation(definition);
            // This representation is static: its existing structural identity covers every declaration.
            string etag = '"' + item.DefinitionHash + '"';
            http.Response.Headers.CacheControl = MetadataCacheControl;
            http.Response.Headers.Vary = HeaderNames.Authorization;
            http.Response.Headers.ETag = etag;
            var conditions = http.Request.GetTypedHeaders().IfNoneMatch;
            if (conditions?.Any(condition => condition.Tag.Value == "*" || condition.Tag.Value == etag) == true)
                return Results.StatusCode(StatusCodes.Status304NotModified);
            return Json(item);
        }
        catch (ResourceRuntimeException failure) { return await RuntimeError(http, failure); }
        catch (ResourceTransportException failure) { return Error(http, StatusCodes.Status400BadRequest, InvalidRequest, resource, failure.Path); }
        catch (FormatException) { return Error(http, StatusCodes.Status400BadRequest, InvalidRequest); }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception) { return Error(http, StatusCodes.Status500InternalServerError, ExecutionFailed); }
    }

    internal static ResourceDefinition Lookup(HttpContext http, string resource) => http.RequestServices.GetRequiredService<ResourceRegistry>().TryGet(resource, out var definition)
        ? definition! : throw new ResourceRuntimeException(resource, ResourceRuntimeError.ResourceNotFound);
    private static string IdentifierText(ScalarValue value) => value switch
    {
        TextValue text => text.Value,
        UuidValue uuid => uuid.Value.ToString("D"),
        NumericValue number => number.Value.CanonicalText,
        _ => throw new InvalidOperationException("Invalid identifier projection.")
    };
    internal static IResult Json<T>(T value, int status = StatusCodes.Status200OK) => Results.Json(value, ResourceTransport.Json, MediaTypeNames.Application.Json, status);
    internal static IResult Error(HttpContext http, int status, string code, string? resource = null, string? path = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Json(new ContractError(code) { Errors = [new(code, Resource: resource, Path: path is null ? null : "/" + path.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal))] }, status);
    }
    internal static async Task<IResult> RuntimeError(HttpContext http, ResourceRuntimeException failure)
    {
        if (failure.Code == ResourceRuntimeError.TenantRequired)
        {
            // Recover the existing authoritative denial reason (including selection
            // required) without changing Runtime or retrying the rejected operation.
            try { await http.RequestServices.GetRequiredService<IRequestTenantContext>().RequireAsync(http.RequestAborted); }
            catch (TenantAccessException tenant) { return Error(http, tenant.ConfigurationFailure ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status403Forbidden, tenant.Code); }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception) { return Error(http, StatusCodes.Status500InternalServerError, ExecutionFailed); }
            return Error(http, StatusCodes.Status403Forbidden, TenantAccessException.Required);
        }
        var (status, code) = failure.Code switch
        {
            ResourceRuntimeError.ActionNotFound => (StatusCodes.Status404NotFound, "resource_action_not_found"),
            ResourceRuntimeError.ActionHandlerNotRegistered => (StatusCodes.Status503ServiceUnavailable, "resource_action_handler_not_registered"),
            ResourceRuntimeError.ActionUnavailable => (StatusCodes.Status409Conflict, "resource_action_unavailable"),
            ResourceRuntimeError.Conflict => (StatusCodes.Status409Conflict, Conflict),
            ResourceRuntimeError.ActionConflict => (StatusCodes.Status409Conflict, "resource_action_conflict"),
            ResourceRuntimeError.ResourceNotFound => (StatusCodes.Status404NotFound, NotFound),
            ResourceRuntimeError.OperationNotSupported => (StatusCodes.Status405MethodNotAllowed, NotSupported),
            ResourceRuntimeError.ValidationFailed => (StatusCodes.Status400BadRequest, InvalidRequest),
            ResourceRuntimeError.Forbidden => (StatusCodes.Status403Forbidden, "forbidden"),
            ResourceRuntimeError.ModuleDisabled => (StatusCodes.Status503ServiceUnavailable, ModuleDisabledResponse.ErrorCode),
            ResourceRuntimeError.RepositoryNotRegistered => (StatusCodes.Status503ServiceUnavailable, RepositoryMissing),
            _ => (StatusCodes.Status500InternalServerError, ExecutionFailed)
        };
        http.Response.Headers.CacheControl = "no-store";
        if (failure.Code == ResourceRuntimeError.OperationNotSupported)
        {
            var definition = Lookup(http, failure.ResourceId);
            http.Response.Headers.Allow = string.Join(", ", definition.Operations.Select(operation => operation switch
            { ResourceOperation.List or ResourceOperation.Get => HttpMethods.Get, ResourceOperation.Create => HttpMethods.Post, ResourceOperation.Update => HttpMethods.Patch, _ => HttpMethods.Delete }).Distinct(StringComparer.Ordinal));
        }
        if (failure.ReasonCode is { } reason && failure.Code is ResourceRuntimeError.Conflict or ResourceRuntimeError.ActionUnavailable or ResourceRuntimeError.ActionConflict)
            return Json(new ContractError(code)
            {
                Errors = failure.Issues.Count == 0
                ? [new(reason, Resource: failure.ResourceId)]
                : failure.Issues.Select(issue => new ContractErrorDetail(reason, Resource: failure.ResourceId,
                    Path: "/" + issue.Path.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal))).ToArray()
            }, status);
        return failure.Issues.Count == 0 ? Error(http, status, code) : Json(new ContractError(code)
        {
            Errors = failure.Issues.Select(issue => new ContractErrorDetail(JsonNamingPolicy.SnakeCaseLower.ConvertName(Enum.GetName(issue.Code)!), Resource: failure.ResourceId,
                Path: "/" + issue.Path.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal))).ToArray()
        }, status);
    }
}
