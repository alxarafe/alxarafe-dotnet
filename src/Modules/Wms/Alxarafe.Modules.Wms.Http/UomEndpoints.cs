using System.Net.Mime;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alxarafe.AspNetCore;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Alxarafe.Security.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Alxarafe.Modules.Wms.Http;

public static class UomEndpoints
{
    public const string Path = "/api/wms/uoms";
    public const string CodeField = "code";
    public const string DescriptionField = "description";
    public const string InvalidRequest = "invalid_request";
    public const string CodeConflict = "wms_uom_code_conflict";
    private static readonly IReadOnlyList<CollectionOrder> CodeAscending = Array.AsReadOnly(new CollectionOrder[] { new(CodeField, "asc") });

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).RequireAuthorization(CoreAdministration.Policy)
            .WithSharedContractErrors().WithTags("WMS");
        group.MapPost("", CreateAsync).Accepts<CreateUomRequest>(MediaTypeNames.Application.Json)
            .Produces<Uom>(StatusCodes.Status201Created).Produces<UomError>(StatusCodes.Status400BadRequest)
            .Produces<UomError>(StatusCodes.Status401Unauthorized).Produces<UomError>(StatusCodes.Status403Forbidden)
            .Produces<UomError>(StatusCodes.Status409Conflict);
        group.MapGet("", ListAsync).Produces<UomPage>()
            .Produces<UomError>(StatusCodes.Status400BadRequest).Produces<UomError>(StatusCodes.Status401Unauthorized)
            .Produces<UomError>(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> CreateAsync(HttpContext context, UomCatalogue catalogue)
    {
        if (context.Request.Query.Count != 0) return Error(StatusCodes.Status400BadRequest, InvalidRequest);
        string? code;
        string? description;
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2
                || !root.TryGetProperty(CodeField, out var codeValue) || codeValue.ValueKind != JsonValueKind.String
                || !root.TryGetProperty(DescriptionField, out var descriptionValue) || descriptionValue.ValueKind != JsonValueKind.String)
                return Error(StatusCodes.Status400BadRequest, InvalidRequest);
            // Two properties and both exact required names also exclude duplicates/additions.
            code = codeValue.GetString();
            description = descriptionValue.GetString();
        }
        catch (JsonException) { return Error(StatusCodes.Status400BadRequest, InvalidRequest); }
        catch (InvalidOperationException) { return Error(StatusCodes.Status400BadRequest, InvalidRequest); }
        catch (BadHttpRequestException) { return Error(StatusCodes.Status400BadRequest, InvalidRequest); }
        var result = await catalogue.CreateAsync(code, description, context.RequestAborted);
        return result.Outcome switch
        {
            CreateUomOutcome.Created => Results.Json(result.Uom, statusCode: StatusCodes.Status201Created),
            CreateUomOutcome.CodeConflict => Error(StatusCodes.Status409Conflict, CodeConflict),
            _ => Error(StatusCodes.Status400BadRequest, InvalidRequest)
        };
    }

    private static async Task<IResult> ListAsync(HttpContext context, UomCatalogue catalogue)
    {
        if (context.Request.Query.Any(entry => entry.Value.Count != 1
            || entry.Key is not (CollectionPagination.OffsetParameter or CollectionPagination.LimitParameter)))
            return Error(StatusCodes.Status400BadRequest, InvalidRequest);
        var pagination = CollectionPagination.Read(context.Request.Query);
        if (pagination is null) return Error(StatusCodes.Status400BadRequest, InvalidRequest);
        var window = await catalogue.ListAsync(pagination.Offset, pagination.Limit, context.RequestAborted);
        return Results.Json(new UomPage(window.Items, pagination.Offset, pagination.Limit, window.Total, CodeAscending));
    }

    private static IResult Error(int status, string code) => Results.Json(new UomError(code), statusCode: status);
}

public sealed record UomError(string Code);
public sealed record CreateUomRequest(string Code, string Description);
public sealed record UomPage(IReadOnlyList<Uom> Items, [property: JsonConverter(typeof(OffsetJsonConverter))] BigInteger Offset,
    int Limit, long Total, IReadOnlyList<CollectionOrder> Order);
