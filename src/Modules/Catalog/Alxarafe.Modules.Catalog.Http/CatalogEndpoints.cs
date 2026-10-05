using Alxarafe.Modules.Catalog.Application;
using Alxarafe.Modules.Catalog.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace Alxarafe.Modules.Catalog.Http;

public static class CatalogEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/catalog");
        group.MapPost("/items", async (CreateItemRequest request, CreateItemHandler handler, CancellationToken ct) =>
        {
            var item = await handler.HandleAsync(new CreateItemCommand(request.Sku, request.Name), ct);
            return Results.Created($"/api/catalog/items/{item.Id}", item);
        }).RequireAuthorization(CatalogPermissions.ItemsCreate).WithTags("Catalog");
        group.MapGet("/items/{id:guid}", async (Guid id, GetItemByIdHandler handler, IStringLocalizer<CatalogMessages> localizer, CancellationToken ct) =>
        {
            var item = await handler.HandleAsync(new ItemId(id), ct);
            return item is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: localizer["catalog.item_not_found"].Value, extensions: new Dictionary<string, object?> { ["code"] = "catalog.item_not_found" })
                : Results.Ok(item);
        }).RequireAuthorization(CatalogPermissions.ItemsRead).WithTags("Catalog");
    }
}

public static class CatalogPermissions
{
    public const string ItemsRead = "catalog.items.read";
    public const string ItemsCreate = "catalog.items.create";
}

public sealed class CatalogMessages { }
public sealed record CreateItemRequest(string Sku, string Name);
