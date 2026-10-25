using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Numerics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Alxarafe.Modules.Wms.Http;
using Alxarafe.Modules.Wms.Infrastructure;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class InventoryTests(TenantApplicationFactory factory) : IClassFixture<TenantApplicationFactory>, IAsyncLifetime
{
    private const string Password = "inventory-fixture-password";
    private const string UomCode = "IVUN";
    private const string OtherUomCode = "IVKG";
    private static readonly DateTimeOffset Time = new(2026, 6, 20, 9, 0, 0, TimeSpan.Zero);
    private readonly string code = "IV-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
    private readonly List<Guid> users = [];
    private HttpClient client = null!;
    private string admin = "", ordinary = "", reader = "", writer = "";
    private IDbContextFactory<WmsDbContext> contexts = null!;
    private InventoryService service = null!;
    private IInventoryStore store = null!;
    private Warehouse warehouse = null!;
    private Location location = null!;
    private StockItem item = null!;
    private Uom uom = null!;
    private Guid otherUom;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        contexts = factory.Services.GetRequiredService<IDbContextFactory<WmsDbContext>>();
        service = factory.Services.GetRequiredService<InventoryService>();
        store = factory.Services.GetRequiredService<IInventoryStore>();
        await factory.Services.GetRequiredService<ModuleStateService>().EnableAsync(WmsModule.Id);
        admin = await UserAsync(true);
        ordinary = await UserAsync(false);
        reader = await UserAsync(false, InventoryPermissions.MovementsRead, InventoryPermissions.InventoryRead);
        writer = await UserAsync(false, InventoryPermissions.MovementsWrite);
        uom = (await factory.Services.GetRequiredService<UomCatalogue>().CreateAsync(UomCode, "Inventory unit")).Uom!;
        otherUom = (await factory.Services.GetRequiredService<UomCatalogue>().CreateAsync(OtherUomCode, "Other unit")).Uom!.Id;
        warehouse = (await service.CreateWarehouseAsync(code, "Warehouse", default)).Value!;
        location = (await service.CreateLocationAsync(warehouse.Id, "A.01.01", true, default)).Value!;
        item = (await service.CreateItemAsync(code, "Reference", uom.Id, null, default)).Value!;
    }
    public async Task DisposeAsync()
    {
        await factory.Services.GetRequiredService<ModuleStateService>().EnableAsync(WmsModule.Id);
        await using var db = await contexts.CreateDbContextAsync();
        var warehouses = db.Set<Warehouse>().Where(row => row.Code.StartsWith(code)).Select(row => row.Id);
        await db.Set<InventoryMovement>().Where(row => warehouses.Contains(row.WarehouseId)).ExecuteDeleteAsync();
        await db.Set<Location>().Where(row => warehouses.Contains(row.WarehouseId)).ExecuteDeleteAsync();
        await db.Set<StockItem>().Where(row => row.Sku.StartsWith(code)).ExecuteDeleteAsync();
        await db.Set<Warehouse>().Where(row => warehouses.Contains(row.Id)).ExecuteDeleteAsync();
        await db.Uoms.Where(row => row.Code == UomCode || row.Code == OtherUomCode).ExecuteDeleteAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(row => users.Contains(row.Id)).ExecuteDeleteAsync();
        client.Dispose();
    }
    private async Task<string> UserAsync(bool isAdmin, params string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserAdministration>().CreateAsync(
            Guid.NewGuid().ToString("N") + "@example.test", Password, isAdmin))!;
        users.Add(user.Id);
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
        var identity = (await manager.FindByIdAsync(user.Id.ToString()))!;
        Assert.True((await manager.AddClaimsAsync(identity, permissions.Select(permission => new Claim(PermissionClaim.Type, permission)))).Succeeded);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Body(response)).GetProperty("accessToken").GetString()!;
    }
    private object Movement(string key, decimal delta = 1.25m, Guid? atLocation = null, Guid? atWarehouse = null, Guid? unit = null) =>
        new
        {
            warehouseId = atWarehouse ?? warehouse.Id,
            itemId = item.Id,
            locationId = atLocation ?? location.Id,
            occurredAt = Time,
            quantityDelta = delta,
            uomId = unit ?? uom.Id,
            type = delta > 0 ? InventoryRules.Receipt : InventoryRules.Issue,
            externalKey = key
        };
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if ((token ?? admin).Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? admin);
        if (body is not null) request.Content = body is string text ? new StringContent(text, Encoding.UTF8, "application/json") : JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    private static Task<JsonElement> Body(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();
    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        ContractErrors.Single(await Body(response), code);
    }

    [Fact]
    public async Task ConcurrentRetriesCommitOneHistoricalFactAndDistinctMovementsNeverLoseQuantities()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Send(HttpMethod.Post, InventoryEndpoints.Movements, Movement("RETRY"), writer)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        var ids = await Task.WhenAll(responses.Select(Body));
        Assert.Single(ids.Select(body => body.GetProperty("id").GetGuid()).Distinct());
        await Error(await Send(HttpMethod.Post, InventoryEndpoints.Movements, Movement("RETRY", 2)), HttpStatusCode.Conflict, InventoryEndpoints.Conflict);
        var writes = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Send(HttpMethod.Post, InventoryEndpoints.Movements, Movement("NEW-" + i), writer)));
        Assert.All(writes, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        await using var db = await contexts.CreateDbContextAsync();
        Assert.Equal(9, await db.Set<InventoryMovement>().CountAsync(row => row.WarehouseId == warehouse.Id));
        var page = await store.BalancesAsync(new(warehouse.Id), BigInteger.Zero, 10, default);
        Assert.Equal(11.25m, Assert.Single(page.Items).Quantity);
        var http = await Body(await Send(HttpMethod.Get, InventoryEndpoints.Inventory + "?warehouseId=" + warehouse.Id, token: reader));
        Assert.Equal(11.25m, Assert.Single(http.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task ForeignKeysUnicityDisabledLocationsAndBaseUomAreEnforced()
    {
        await Error(await Send(HttpMethod.Post, InventoryEndpoints.Warehouses, new { code, name = "Changed" }), HttpStatusCode.Conflict, InventoryEndpoints.Conflict);
        await Error(await Send(HttpMethod.Post, InventoryEndpoints.Items, new { sku = code, description = "Changed", baseUomId = uom.Id }), HttpStatusCode.Conflict, InventoryEndpoints.Conflict);
        await Error(await Send(HttpMethod.Post, InventoryEndpoints.Locations, new { warehouseId = warehouse.Id, code = location.Code, enabled = true }), HttpStatusCode.Conflict, InventoryEndpoints.Conflict);
        await Error(await Send(HttpMethod.Post, InventoryEndpoints.Items, new { sku = code + "-MISSING", description = "Reference", baseUomId = Guid.NewGuid() }), HttpStatusCode.BadRequest, InventoryEndpoints.ReferenceNotFound);
        var other = (await service.CreateWarehouseAsync(code + "-OTHER", "Other", default)).Value!;
        Assert.Equal(InventoryOutcome.Created, (await service.CreateLocationAsync(other.Id, location.Code, true, default)).Outcome);
        await Error(await Send(HttpMethod.Post, InventoryEndpoints.Movements, Movement("WRONG-WH", atWarehouse: other.Id)), HttpStatusCode.BadRequest, InventoryEndpoints.ReferenceNotFound);
        await Error(await Send(HttpMethod.Post, InventoryEndpoints.Movements, Movement("WRONG-UOM", unit: otherUom)), HttpStatusCode.BadRequest, InventoryEndpoints.ReferenceNotFound);
        var disabled = (await service.CreateLocationAsync(warehouse.Id, "DISABLED", false, default)).Value!;
        await Error(await Send(HttpMethod.Post, InventoryEndpoints.Movements, Movement("DISABLED", atLocation: disabled.Id)), HttpStatusCode.Conflict, InventoryEndpoints.LocationDisabled);
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, InventoryEndpoints.Movements, Movement("VALID"))).StatusCode);
        await using var db = await contexts.CreateDbContextAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Set<Location>().Where(row => row.Id == location.Id).ExecuteDeleteAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task NaturalResolutionPaginationFiltersAndHalfOpenBusinessTimeAreStable()
    {
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, InventoryEndpoints.Movements, Movement("ONE"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, InventoryEndpoints.Movements, Movement("TWO", -0.25m))).StatusCode);
        foreach (var (path, query, id) in new[] { (InventoryEndpoints.Warehouses, "code=" + code, warehouse.Id),
            (InventoryEndpoints.Locations, "warehouseId=" + warehouse.Id + "&code=" + location.Code, location.Id),
            (InventoryEndpoints.Items, "sku=" + code, item.Id) })
        {
            var page = await Body(await Send(HttpMethod.Get, path + "?" + query));
            Assert.Equal(id, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
            Assert.Equal(id, (await Body(await Send(HttpMethod.Get, path + "/" + id))).GetProperty("id").GetGuid());
        }
        string filter = "?warehouseId=" + warehouse.Id + "&itemId=" + item.Id + "&locationId=" + location.Id + "&sku=" + code;
        var moves = await Body(await Send(HttpMethod.Get, InventoryEndpoints.Movements + filter + "&limit=1&offset=1&from=2026-06-20T09:00:00Z&to=2026-06-21T00:00:00Z"));
        Assert.Equal(2, moves.GetProperty("total").GetInt32());
        Assert.Single(moves.GetProperty("items").EnumerateArray());
        var excluded = await Body(await Send(HttpMethod.Get, InventoryEndpoints.Movements + filter + "&to=2026-06-20T09:00:00Z"));
        Assert.Equal(0, excluded.GetProperty("total").GetInt32());
        var zero = await Body(await Send(HttpMethod.Get, InventoryEndpoints.Inventory + filter + "&offset=99999999999999999999999"));
        Assert.Equal(1, zero.GetProperty("total").GetInt32());
        Assert.Empty(zero.GetProperty("items").EnumerateArray());
        var balance = await Body(await Send(HttpMethod.Get, InventoryEndpoints.Inventory + filter));
        Assert.Equal(1m, Assert.Single(balance.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetDecimal());
        await Error(await Send(HttpMethod.Get, InventoryEndpoints.Items + "/" + Guid.NewGuid()), HttpStatusCode.NotFound, InventoryEndpoints.NotFound);
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, InventoryEndpoints.Movements, Movement("ZERO", -1m))).StatusCode);
        Assert.Empty((await store.BalancesAsync(new(warehouse.Id), BigInteger.Zero, 10, default)).Items);
    }

    [Fact]
    public async Task EachFilterExcludesOtherWarehousesReferencesAndLocations()
    {
        var secondLocation = (await service.CreateLocationAsync(warehouse.Id, "A.01.02", true, default)).Value!;
        var secondWarehouse = (await service.CreateWarehouseAsync(code + "-OTHER", "Other", default)).Value!;
        var remoteLocation = (await service.CreateLocationAsync(secondWarehouse.Id, location.Code, true, default)).Value!;
        var secondItem = (await service.CreateItemAsync(code + "-OTHER", "Other item", uom.Id, null, default)).Value!;
        var facts = new[]
        {
            (await service.RecordAsync(warehouse.Id, item.Id, location.Id, Time, 1, uom.Id, InventoryRules.Receipt, "FILTER-ONE", default)).Value!,
            (await service.RecordAsync(warehouse.Id, item.Id, secondLocation.Id, Time, 2, uom.Id, InventoryRules.Receipt, "FILTER-TWO", default)).Value!,
            (await service.RecordAsync(warehouse.Id, secondItem.Id, location.Id, Time, 3, uom.Id, InventoryRules.Receipt, "FILTER-THREE", default)).Value!,
            (await service.RecordAsync(secondWarehouse.Id, item.Id, remoteLocation.Id, Time, 4, uom.Id, InventoryRules.Receipt, "FILTER-FOUR", default)).Value!
        };
        var cases = new (string Query, int[] Indexes)[]
        {
            ("warehouseId=" + warehouse.Id, [0, 1, 2]),
            ("itemId=" + item.Id, [0, 1, 3]),
            ("locationId=" + location.Id, [0, 2]),
            ("sku=" + code, [0, 1, 3]),
            ("warehouseId=" + warehouse.Id + "&sku=" + code + "&locationId=" + location.Id, [0]),
            ("warehouseId=" + Guid.NewGuid(), [])
        };
        foreach (var (query, indexes) in cases)
        {
            var movements = await Body(await Send(HttpMethod.Get, InventoryEndpoints.Movements + "?" + query));
            Assert.Equal(indexes.Select(index => facts[index].Id).Order(), movements.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).Order());
            var inventory = await Body(await Send(HttpMethod.Get, InventoryEndpoints.Inventory + "?" + query));
            Assert.Equal(indexes.Select(index => facts[index].QuantityDelta).Order(), inventory.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("quantity").GetDecimal()).Order());
        }
    }

    [Theory]
    [InlineData("warehouseId=invalid")]
    [InlineData("itemId=00000000-0000-0000-0000-000000000000")]
    [InlineData("locationId=")]
    [InlineData("sku=lower")]
    [InlineData("limit=0")]
    [InlineData("offset=-1")]
    [InlineData("sku=A&sku=B")]
    [InlineData("from=2026-06-20")]
    [InlineData("from=June%2020,%202026%20%2B00:00")]
    [InlineData("from=2026-06-20T09:00:00.0000001Z")]
    [InlineData("from=2026-06-21T00:00:00Z&to=2026-06-20T00:00:00Z")]
    public async Task InvalidFiltersFailBeforeQueryingInventory(string query) =>
        await Error(await Send(HttpMethod.Get, InventoryEndpoints.Movements + "?" + query), HttpStatusCode.BadRequest, InventoryEndpoints.InvalidRequest);

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"code\":\"A\",\"name\":\"B\",\"extra\":1}")]
    [InlineData("{\"code\":\"A\",\"code\":\"B\",\"name\":\"B\"}")]
    [InlineData("{\"Code\":\"A\",\"name\":\"B\"}")]
    public async Task InvalidJsonProducesClosedContractError(string json) =>
        await Error(await Send(HttpMethod.Post, InventoryEndpoints.Warehouses, json), HttpStatusCode.BadRequest, InventoryEndpoints.InvalidRequest);

    [Fact]
    public async Task SecurityAndModuleGatePrecedeParsingAndPermissionsRemainIndependent()
    {
        foreach (bool enabled in new[] { true, false })
        {
            if (!enabled) await factory.Services.GetRequiredService<ModuleStateService>().DisableAsync(WmsModule.Id);
            foreach (string path in new[] { InventoryEndpoints.Warehouses, InventoryEndpoints.Locations, InventoryEndpoints.Items, InventoryEndpoints.Movements, InventoryEndpoints.Inventory })
            {
                await Error(await Send(HttpMethod.Get, path + "?unknown=x", token: ""), HttpStatusCode.Unauthorized, "unauthorized");
                await Error(await Send(HttpMethod.Get, path + "?unknown=x", token: ordinary), HttpStatusCode.Forbidden, "forbidden");
                await Error(await Send(HttpMethod.Get, path + "?unknown=x"), enabled ? HttpStatusCode.BadRequest : HttpStatusCode.ServiceUnavailable,
                    enabled ? InventoryEndpoints.InvalidRequest : ModuleDisabledResponse.ErrorCode);
            }
            await Error(await Send(HttpMethod.Post, InventoryEndpoints.Movements, "{", reader), HttpStatusCode.Forbidden, "forbidden");
            await Error(await Send(HttpMethod.Get, InventoryEndpoints.Movements, token: writer), HttpStatusCode.Forbidden, "forbidden");
            await Error(await Send(HttpMethod.Post, InventoryEndpoints.Movements, "{", writer), enabled ? HttpStatusCode.BadRequest : HttpStatusCode.ServiceUnavailable,
                enabled ? InventoryEndpoints.InvalidRequest : ModuleDisabledResponse.ErrorCode);
        }
    }
}
