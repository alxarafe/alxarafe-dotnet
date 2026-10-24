using System.Globalization;
using System.Net.Mime;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Alxarafe.AspNetCore;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Alxarafe.Security.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Alxarafe.Modules.Wms.Http;

// Implementation-specific public APIs. Not part of published ERBAS v0.11.1 and
// deliberately absent from capability discovery/invocation and shared Bruno.
public static class InventoryEndpoints
{
    public const string Warehouses = "/api/wms/warehouses";
    public const string Locations = "/api/wms/locations";
    public const string Items = "/api/wms/items";
    public const string Movements = "/api/wms/movements";
    public const string Inventory = "/api/wms/inventory";
    public const string InvalidRequest = "invalid_request";
    public const string Conflict = "wms_inventory_conflict";
    public const string ReferenceNotFound = "wms_inventory_reference_not_found";
    public const string NotFound = "wms_inventory_not_found";
    public const string LocationDisabled = "wms_location_disabled";
    private const string WarehouseId = "warehouseId", ItemId = "itemId", LocationId = "locationId", Code = "code", Sku = "sku",
        From = "from", To = "to", OccurredAt = "occurredAt", Ascending = "asc";
    private static readonly string[] StockFilters = [WarehouseId, ItemId, LocationId, Sku];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK";
    private static readonly Regex TimestampOffset = new("(Z|[+-][0-9]{2}:[0-9]{2})\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        MapResource<WarehouseRequest, Warehouse>(endpoints, Warehouses, InventoryPermissions.WarehousesRead, InventoryPermissions.WarehousesWrite,
            (request, service, ct) => service.CreateWarehouseAsync(request.Code, request.Name, ct),
            (store, filter, offset, limit, ct) => store.WarehousesAsync(filter, offset, limit, ct), [Code], [Code]);
        MapResource<LocationRequest, Location>(endpoints, Locations, InventoryPermissions.LocationsRead, InventoryPermissions.LocationsWrite,
            (request, service, ct) => service.CreateLocationAsync(request.WarehouseId, request.Code, request.Enabled, ct),
            (store, filter, offset, limit, ct) => store.LocationsAsync(filter, offset, limit, ct), [WarehouseId, Code], [WarehouseId, Code]);
        MapResource<ItemRequest, StockItem>(endpoints, Items, InventoryPermissions.ItemsRead, InventoryPermissions.ItemsWrite,
            (request, service, ct) => service.CreateItemAsync(request.Sku, request.Description, request.BaseUomId, request.ItemFamilyId, ct),
            (store, filter, offset, limit, ct) => store.ItemsAsync(filter, offset, limit, ct), [Sku], [Sku]);
        MapResource<MovementRequest, InventoryMovement>(endpoints, Movements, InventoryPermissions.MovementsRead, InventoryPermissions.MovementsWrite,
            (request, service, ct) => service.RecordAsync(request.WarehouseId, request.ItemId, request.LocationId, request.OccurredAt,
                request.QuantityDelta, request.UomId, request.Type, request.ExternalKey, ct),
            (store, filter, offset, limit, ct) => store.MovementsAsync(filter, offset, limit, ct), [.. StockFilters, From, To], [OccurredAt, "id"]);
        var group = Group(endpoints, Inventory);
        group.MapGet("", async (HttpContext context, IInventoryStore store) =>
            await ListAsync(context, store, (s, f, o, l, ct) => s.BalancesAsync(f, o, l, ct), StockFilters, [WarehouseId, ItemId, LocationId]))
            .RequireAuthorization(InventoryPermissions.InventoryRead).Produces<InventoryPage<InventoryBalance>>();
    }

    private static RouteGroupBuilder Group(IEndpointRouteBuilder endpoints, string path) =>
        endpoints.MapGroup(path).WithSharedContractErrors().WithTags("WMS implementation-specific");

    private static void MapResource<TRequest, T>(IEndpointRouteBuilder endpoints, string path, string read, string write,
        Func<TRequest, InventoryService, CancellationToken, Task<InventoryResult<T>>> create,
        Func<IInventoryStore, InventoryFilter, BigInteger, int, CancellationToken, Task<InventoryWindow<T>>> list,
        string[] filters, string[] order) where TRequest : class
    {
        var group = Group(endpoints, path);
        group.MapPost("", async (HttpContext context, InventoryService service) =>
        {
            var request = await ReadAsync<TRequest>(context);
            if (request is null) return Error(StatusCodes.Status400BadRequest, InvalidRequest);
            var result = await create(request, service, context.RequestAborted);
            return result.Outcome switch
            {
                InventoryOutcome.Created => Results.Json(result.Value, statusCode: StatusCodes.Status201Created),
                InventoryOutcome.Replayed => Results.Json(result.Value),
                InventoryOutcome.Conflict => Error(StatusCodes.Status409Conflict, Conflict),
                InventoryOutcome.ReferenceNotFound => Error(StatusCodes.Status400BadRequest, ReferenceNotFound),
                InventoryOutcome.LocationDisabled => Error(StatusCodes.Status409Conflict, LocationDisabled),
                _ => Error(StatusCodes.Status400BadRequest, InvalidRequest)
            };
        }).RequireAuthorization(write).Accepts<TRequest>(MediaTypeNames.Application.Json)
            .Produces<T>(StatusCodes.Status201Created).Produces<T>().Produces<ContractError>(StatusCodes.Status400BadRequest)
            .Produces<ContractError>(StatusCodes.Status409Conflict);
        group.MapGet("", async (HttpContext context, IInventoryStore store) => await ListAsync(context, store, list, filters, order))
            .RequireAuthorization(read).Produces<InventoryPage<T>>();
        group.MapGet("/{id:guid}", async (Guid id, HttpContext context, IInventoryStore store) =>
        {
            if (context.Request.Query.Count != 0 || id == Guid.Empty) return Error(StatusCodes.Status400BadRequest, InvalidRequest);
            var result = await list(store, new InventoryFilter(Id: id), BigInteger.Zero, 1, context.RequestAborted);
            return result.Items.Count == 0 ? Error(StatusCodes.Status404NotFound, NotFound) : Results.Json(result.Items[0]);
        }).RequireAuthorization(read).Produces<T>().Produces<ContractError>(StatusCodes.Status404NotFound);
    }

    private static async Task<T?> ReadAsync<T>(HttpContext context) where T : class
    {
        if (context.Request.Query.Count != 0 || !context.Request.HasJsonContentType()) return null;
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var properties = root.EnumerateObject().ToArray();
            if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length) return null;
            if (root.TryGetProperty(OccurredAt, out var timestamp)
                && (timestamp.ValueKind != JsonValueKind.String || !TimestampOffset.IsMatch(timestamp.GetString()!))) return null;
            return root.Deserialize<T>(Json);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (BadHttpRequestException) { return null; }
    }

    private static async Task<IResult> ListAsync<T>(HttpContext context, IInventoryStore store,
        Func<IInventoryStore, InventoryFilter, BigInteger, int, CancellationToken, Task<InventoryWindow<T>>> list, string[] filters, string[] order)
    {
        var query = context.Request.Query;
        if (query.Any(entry => entry.Value.Count != 1 || !(filters.Contains(entry.Key, StringComparer.Ordinal)
            || entry.Key is CollectionPagination.OffsetParameter or CollectionPagination.LimitParameter)))
            return Error(StatusCodes.Status400BadRequest, InvalidRequest);
        var pagination = CollectionPagination.Read(query);
        if (pagination is null) return Error(StatusCodes.Status400BadRequest, InvalidRequest);
        Guid? warehouse = null, item = null, location = null;
        DateTimeOffset? from = null, to = null;
        if (!ReadId(WarehouseId, out warehouse) || !ReadId(ItemId, out item) || !ReadId(LocationId, out location)
            || !ReadTime(From, out from) || !ReadTime(To, out to) || (from.HasValue && to.HasValue && from >= to)
            || query.ContainsKey(Code) && !InventoryRules.ValidCode(query[Code]) || query.ContainsKey(Sku) && !InventoryRules.ValidCode(query[Sku]))
            return Error(StatusCodes.Status400BadRequest, InvalidRequest);
        var filter = new InventoryFilter(warehouse, item, location, query.ContainsKey(Code) ? query[Code].ToString() : null,
            query.ContainsKey(Sku) ? query[Sku].ToString() : null, from, to);
        var window = await list(store, filter, pagination.Offset, pagination.Limit, context.RequestAborted);
        return Results.Json(new InventoryPage<T>(window.Items, pagination.Offset, pagination.Limit, window.Total,
            order.Select(field => new CollectionOrder(field, Ascending)).ToArray()));

        bool ReadId(string key, out Guid? value)
        {
            value = null;
            if (!query.ContainsKey(key)) return true;
            if (!Guid.TryParseExact(query[key], "D", out var id) || id == Guid.Empty) return false;
            value = id;
            return true;
        }
        bool ReadTime(string key, out DateTimeOffset? value)
        {
            value = null;
            if (!query.ContainsKey(key)) return true;
            string raw = query[key].ToString();
            if (!TimestampOffset.IsMatch(raw) || !DateTimeOffset.TryParseExact(raw, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)
                || timestamp.Ticks % TimeSpan.TicksPerMicrosecond != 0) return false;
            value = timestamp.ToUniversalTime();
            return true;
        }
    }
    private static IResult Error(int status, string code) => Results.Json(new ContractError(code), statusCode: status);
}

public sealed record WarehouseRequest([property: JsonRequired] string Code, [property: JsonRequired] string Name);
public sealed record LocationRequest([property: JsonRequired] Guid WarehouseId, [property: JsonRequired] string Code, [property: JsonRequired] bool Enabled);
public sealed record ItemRequest([property: JsonRequired] string Sku, [property: JsonRequired] string Description,
    [property: JsonRequired] Guid BaseUomId, Guid? ItemFamilyId = null);
public sealed record MovementRequest([property: JsonRequired] Guid WarehouseId, [property: JsonRequired] Guid ItemId,
    [property: JsonRequired] Guid LocationId, [property: JsonRequired] DateTimeOffset OccurredAt,
    [property: JsonRequired] decimal QuantityDelta, [property: JsonRequired] Guid UomId,
    [property: JsonRequired] string Type, [property: JsonRequired] string ExternalKey);
public sealed record InventoryPage<T>(IReadOnlyList<T> Items, [property: JsonConverter(typeof(OffsetJsonConverter))] BigInteger Offset,
    int Limit, long Total, IReadOnlyList<CollectionOrder> Order);
