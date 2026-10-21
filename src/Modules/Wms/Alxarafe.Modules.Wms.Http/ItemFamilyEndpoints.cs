using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Alxarafe.AspNetCore;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Alxarafe.Security.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;

namespace Alxarafe.Modules.Wms.Http;

public static class ItemFamilyEndpoints
{
    public const string Path = "/api/wms/item-families";
    public const string CodeField = "code";
    public const string NameField = "name";
    public const string InvalidRequest = "invalid_request";
    public const string CodeConflict = "wms_item_family_code_conflict";
    private static readonly IReadOnlyList<CollectionOrder> CodeAscending = Array.AsReadOnly(new CollectionOrder[] { new(CodeField, "asc") });

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).RequireAuthorization(CoreAdministration.Policy)
            .WithSharedContractErrors().WithTags("WMS");
        group.MapPost("", CreateAsync).Accepts<CreateItemFamilyRequest>(MediaTypeNames.Application.Json)
            .Produces<ItemFamilyResponse>(StatusCodes.Status201Created).Produces<ItemFamilyError>(StatusCodes.Status400BadRequest)
            .Produces<ItemFamilyError>(StatusCodes.Status401Unauthorized).Produces<ItemFamilyError>(StatusCodes.Status403Forbidden)
            .Produces<ItemFamilyError>(StatusCodes.Status409Conflict);
        group.MapGet("", ListAsync).Produces<ItemFamilyPage>()
            .Produces<ItemFamilyError>(StatusCodes.Status400BadRequest).Produces<ItemFamilyError>(StatusCodes.Status401Unauthorized)
            .Produces<ItemFamilyError>(StatusCodes.Status403Forbidden)
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
    }

    private static async Task<IResult> CreateAsync(HttpContext context, ItemFamilyCatalogue catalogue)
    {
        if (context.Request.Query.Count != 0) return Error(StatusCodes.Status400BadRequest, InvalidRequest);
        string? code;
        string? name;
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2
                || !root.TryGetProperty(CodeField, out var codeValue) || codeValue.ValueKind != JsonValueKind.String
                || !root.TryGetProperty(NameField, out var nameValue) || nameValue.ValueKind != JsonValueKind.String)
                return Error(StatusCodes.Status400BadRequest, InvalidRequest);
            // Two properties and both exact required names also exclude duplicates/additions.
            code = codeValue.GetString();
            name = nameValue.GetString();
        }
        catch (JsonException) { return Error(StatusCodes.Status400BadRequest, InvalidRequest); }
        catch (InvalidOperationException) { return Error(StatusCodes.Status400BadRequest, InvalidRequest); }
        catch (BadHttpRequestException) { return Error(StatusCodes.Status400BadRequest, InvalidRequest); }
        var result = await catalogue.CreateAsync(code, name, context.RequestAborted);
        return result.Outcome switch
        {
            CreateItemFamilyOutcome.Created => Results.Json(ItemFamilyResponse.From(result.ItemFamily!), statusCode: StatusCodes.Status201Created),
            CreateItemFamilyOutcome.CodeConflict => Error(StatusCodes.Status409Conflict, CodeConflict),
            _ => Error(StatusCodes.Status400BadRequest, InvalidRequest)
        };
    }

    private static async Task<IResult> ListAsync(HttpContext context, ItemFamilyCatalogue catalogue)
    {
        if (context.Request.Query.Any(entry => entry.Value.Count != 1
            || entry.Key is not (CollectionPagination.OffsetParameter or CollectionPagination.LimitParameter)))
            return Error(StatusCodes.Status400BadRequest, InvalidRequest);
        var pagination = CollectionPagination.Read(context.Request.Query);
        if (pagination is null) return Error(StatusCodes.Status400BadRequest, InvalidRequest);
        var window = await catalogue.ListAsync(pagination.Offset, pagination.Limit, context.RequestAborted);
        return Results.Json(ToPage(window, pagination));
    }

    public static ItemFamilyPage ToPage(ItemFamilyWindow window, CollectionPagination pagination) =>
        new(window.Items.Select(ItemFamilyResponse.From).ToArray(), pagination.Offset, pagination.Limit, window.Total, CodeAscending);

    private static IResult Error(int status, string code) => Results.Json(new ItemFamilyError(code), statusCode: status);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemFamilyError(string Code) : ContractError(Code);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateItemFamilyRequest(
    [property: Required, StringLength(ItemFamily.CodeMaximum, MinimumLength = 1), RegularExpression(ItemFamily.CodePattern)] string Code,
    [property: Required] string Name);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemFamilyResponse([property: Required] Guid Id,
    [property: Required, StringLength(ItemFamily.CodeMaximum, MinimumLength = 1), RegularExpression(ItemFamily.CodePattern)] string Code,
    [property: Required, StringLength(ItemFamily.NameMaximum, MinimumLength = 1)] string Name)
{
    internal static ItemFamilyResponse From(ItemFamily value) => new(value.Id, value.Code, value.Name);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemFamilyPage([property: Required] IReadOnlyList<ItemFamilyResponse> Items, [property: Required, JsonConverter(typeof(OffsetJsonConverter))] BigInteger Offset,
    [property: Required, Range(CollectionPagination.MinimumLimit, CollectionPagination.MaximumLimit)] int Limit,
    [property: Required] long Total, [property: Required] IReadOnlyList<CollectionOrder> Order);
