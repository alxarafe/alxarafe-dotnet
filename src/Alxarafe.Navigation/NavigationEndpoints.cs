using System.Net.Mime;
using Alxarafe.AspNetCore;
using Alxarafe.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Alxarafe.Security.AspNetCore;

namespace Alxarafe.Navigation;

public static class NavigationEndpoints
{
    private const string InvalidRequest = "invalid_request";
    private const string Unauthorized = "unauthorized";
    private const string ExecutionFailed = "resource_execution_failed";
    public const string Path = "/api/meta/navigation";
    public static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet(Path, (Func<HttpContext, Task<IResult>>)Execute).RequireAuthorization().WithSharedContractErrors()
        .WithTags("Application navigation").Produces<NavigationResponse>().Produces(StatusCodes.Status304NotModified)
        .Produces<ContractError>(StatusCodes.Status400BadRequest).Produces<ContractError>(StatusCodes.Status401Unauthorized)
        .Produces<ContractError>(StatusCodes.Status403Forbidden).Produces<ContractError>(StatusCodes.Status503ServiceUnavailable).Produces<ContractError>(StatusCodes.Status500InternalServerError);
    private static async Task<IResult> Execute(HttpContext http)
    {
        try
        {
            if (http.Request.Query.Count != 0) return Error(http, StatusCodes.Status400BadRequest, InvalidRequest);
            var navigation = await http.RequestServices.GetRequiredService<NavigationService>().ResolveAsync(http.RequestAborted);
            string etag = '"' + navigation.Version + '"';
            http.Response.Headers.CacheControl = "private, no-cache";
            http.Response.Headers.Vary = "Authorization, Accept-Language";
            http.Response.Headers.ETag = etag;
            var conditions = http.Request.GetTypedHeaders().IfNoneMatch;
            if (conditions?.Any(condition => condition.Tag.Value == "*" || condition.Tag.Value == etag) == true) return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.Json(navigation, NavigationService.Json, MediaTypeNames.Application.Json);
        }
        catch (UnauthorizedAccessException) { http.Response.Headers.WWWAuthenticate = "Bearer"; return Error(http, StatusCodes.Status401Unauthorized, Unauthorized); }
        catch (TenantAccessException failure) { return Error(http, failure.ConfigurationFailure ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status403Forbidden, failure.Code); }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception) { return Error(http, StatusCodes.Status500InternalServerError, ExecutionFailed); }
    }
    private static IResult Error(HttpContext http, int status, string code)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(new ContractError(code), NavigationService.Json, MediaTypeNames.Application.Json, status);
    }
}
