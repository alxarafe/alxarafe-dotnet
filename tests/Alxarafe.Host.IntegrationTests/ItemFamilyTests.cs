using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Alxarafe.Modules.Wms.Http;
using Alxarafe.Modules.Wms.Infrastructure;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed partial class ItemFamilyTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string Password = "wms-native-fixture-password";
    private const string LoginPath = "/api/auth/login";
    private const string BearerScheme = "Bearer";
    private const string Each = "EA";
    private const string Box = "BOX";
    private const string GateUom = "SAGATE";
    private const string UomMigrationId = "202610140001_Uoms";
    private const string Race = "RACE";
    private static readonly string[] OrderedCodes = ["A1", "A10", "A2", "A_1", "B"];
    private static readonly string[] OwnedCodes = [Each, Box, Race, .. OrderedCodes];
    private readonly List<Guid> ownedUsers = [];
    private readonly List<Guid> ownedAttributes = [];
    private HttpClient client = null!;
    private string adminToken = "";
    private string userToken = "";
    private ItemFamilyCatalogue catalogue = null!;
    private IDbContextFactory<WmsDbContext> contexts = null!;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        contexts = factory.Services.GetRequiredService<IDbContextFactory<WmsDbContext>>();
        catalogue = factory.Services.GetRequiredService<ItemFamilyCatalogue>();
        await factory.Services.GetRequiredService<ModuleStateService>().EnableAsync(WmsModule.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserAdministration>();
        foreach (bool admin in new[] { true, false })
        {
            var user = (await users.CreateAsync("native-family-" + Guid.NewGuid().ToString("N") + "@example.test", Password, admin))!;
            ownedUsers.Add(user.Id);
            var login = await client.PostAsJsonAsync(LoginPath, new { email = user.Email, password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            string token = (await Body(login)).GetProperty("accessToken").GetString()!;
            if (admin) adminToken = token;
            else userToken = token;
        }
    }

    public async Task DisposeAsync()
    {
        await factory.Services.GetRequiredService<ModuleStateService>().EnableAsync(WmsModule.Id);
        await using var context = await contexts.CreateDbContextAsync();
        var families = context.ItemFamilies.Where(row => OwnedCodes.Contains(row.Code)).Select(row => row.Id);
        await context.ItemFamilyAssociations.Where(pair => families.Contains(pair.ItemFamilyId)).ExecuteDeleteAsync();
        await context.ItemFamilies.Where(row => OwnedCodes.Contains(row.Code)).ExecuteDeleteAsync();
        await context.StorageAttributes.Where(row => ownedAttributes.Contains(row.Id)).ExecuteDeleteAsync();
        await context.Uoms.Where(row => row.Code == GateUom).ExecuteDeleteAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(user => ownedUsers.Contains(user.Id)).ExecuteDeleteAsync();
        client.Dispose();
    }

    [Fact]
    public async Task EfPersistsNormalizedSupplementaryUnicodeAndOrdersBeforeSqlPagination()
    {
        foreach (string code in OrderedCodes.Reverse()) Assert.Equal(CreateItemFamilyOutcome.Created, (await catalogue.CreateAsync(code, code)).Outcome);
        string maximum = string.Concat(Enumerable.Repeat("📦", ItemFamily.NameMaximum));
        var created = (await catalogue.CreateAsync(Each, " \t" + maximum + "\r\n ")).ItemFamily!;
        await using var context = await contexts.CreateDbContextAsync();
        var persisted = await context.ItemFamilies.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        Assert.Equal(maximum, persisted.Name);
        Assert.NotEqual(Guid.Empty, persisted.Id);
        var page = await catalogue.ListAsync(BigInteger.One, 2);
        Assert.Equal(["A10", "A2"], page.Items.Select(attribute => attribute.Code));
        Assert.Equal(OrderedCodes.Length + 1, page.Total);
        foreach (var offset in new[] { new BigInteger(page.Total), BigInteger.Pow(10, 30) })
        {
            var empty = await catalogue.ListAsync(offset, CollectionPagination.MaximumLimit);
            Assert.Empty(empty.Items);
            Assert.Equal(page.Total, empty.Total);
        }
    }

    [Fact]
    public async Task PersistentUniqueCodeHandlesConcurrentInsertsWithoutChangingOriginal()
    {
        var results = await Task.WhenAll(catalogue.CreateAsync(Race, " Race "), catalogue.CreateAsync(Race, " Race "));
        Assert.Single(results, result => result.Outcome == CreateItemFamilyOutcome.Created);
        Assert.Single(results, result => result.Outcome == CreateItemFamilyOutcome.CodeConflict);
        await using var context = await contexts.CreateDbContextAsync();
        var original = await context.ItemFamilies.AsNoTracking().SingleAsync(row => row.Code == Race);
        Assert.Equal("Race", original.Name);
        Assert.Equal(CreateItemFamilyOutcome.CodeConflict, (await catalogue.CreateAsync(Race, "Changed")).Outcome);
        await context.Entry(original).ReloadAsync();
        Assert.Equal("Race", original.Name);
        context.ItemFamilies.Add(new ItemFamilyRow { Id = Guid.NewGuid(), Code = Race, Name = "Direct duplicate" });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    [Fact]
    public async Task DatabaseRequiresIdentityCodeAndName()
    {
        var attribute = (await catalogue.CreateAsync(Each, "Each")).ItemFamily!;
        foreach (var row in new[]
        {
            new ItemFamilyRow { Id = Guid.NewGuid(), Code = null!, Name = "Box" },
            new ItemFamilyRow { Id = Guid.NewGuid(), Code = Box, Name = null! },
            new ItemFamilyRow { Id = attribute.Id, Code = Box, Name = "Box" }
        })
        {
            await using var context = await contexts.CreateDbContextAsync();
            context.ItemFamilies.Add(row);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task StrictJsonRejectsInvalidBodiesAndTrailingTokensWithoutMutation()
    {
        foreach (string input in new[]
        {
            "",
            " ",
            "{",
            "[]",
            "true",
            "42",
            "\"scalar\"",
            "null",
            "{}",
            "{\"code\":\"EA\"}",
            "{\"name\":\"Cold\"}",
            "{\"Code\":\"EA\",\"name\":\"Cold\"}",
            "{\"code\":\"EA\",\"name\":\"Cold\",\"extra\":true}",
            "{\"code\":\"EA\",\"code\":\"BOX\",\"name\":\"Cold\"}",
            "{\"code\":\"EA\",\"name\":\"Cold\",\"name\":\"Changed\"}",
            "{\"code\":42,\"name\":\"Cold\"}",
            "{\"code\":true,\"name\":\"Cold\"}",
            "{\"code\":null,\"name\":\"Cold\"}",
            "{\"code\":\"EA\",\"name\":42}",
            "{\"code\":\"EA\",\"name\":false}",
            "{\"code\":\"EA\",\"name\":null}",
            "{\"code\":\"EA\",\"name\":\"Cold\"} {}",
            "{\"code\":\"EA\",\"name\":\"\\ud800\"}",
            "{\"code\":\"EA\",\"name\":\"\\udc00\"}",
            "{\"code\":\"EA\",\"name\":\"\\u0000\"}",
        }) await Error(await Send(HttpMethod.Post, ItemFamilyEndpoints.Path, input),
            HttpStatusCode.BadRequest, ItemFamilyEndpoints.InvalidRequest);
        await using var context = await contexts.CreateDbContextAsync();
        Assert.Empty(await context.ItemFamilies.ToArrayAsync());
    }

    [Fact]
    public async Task StrictQueryReusesCollectionsParser()
    {
        foreach (string query in new[] { "?offset=-1", "?offset=0.5", "?offset=1e2", "?offset=%200", "?offset=",
            "?limit=", "?limit=0", "?sort=code", "?offset=0&offset=1", "?limit=1&limit=1", "?Offset=1" })
            await Error(await Send(HttpMethod.Get, ItemFamilyEndpoints.Path + query),
                HttpStatusCode.BadRequest, ItemFamilyEndpoints.InvalidRequest);
    }

    [Fact]
    public async Task HttpCommitsNormalizedUserTextWithExactEnvelopeAndLocaleIndependentErrors()
    {
        const string name = "\u00a0Standard   📦\u00a0";
        var created = await Send(HttpMethod.Post, ItemFamilyEndpoints.Path, JsonSerializer.Serialize(new { code = Each, name = " \t" + name + "\r\n " }));
        Headers(created, HttpStatusCode.Created);
        var attribute = await Body(created);
        Assert.Equal(["code", "id", "name"], attribute.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(name, attribute.GetProperty(ItemFamilyEndpoints.NameField).GetString());
        Assert.NotEqual(Guid.Empty, attribute.GetProperty("id").GetGuid());
        foreach (string locale in new[] { "en", "es" })
            await Error(await Send(HttpMethod.Post, ItemFamilyEndpoints.Path, JsonSerializer.Serialize(new { code = Each, name = "Changed" }), locale: locale),
                HttpStatusCode.Conflict, ItemFamilyEndpoints.CodeConflict);
        var listing = await Send(HttpMethod.Get, ItemFamilyEndpoints.Path, locale: "es-ES");
        Headers(listing, HttpStatusCode.OK);
        var page = await Body(listing);
        Assert.Equal(["items", "limit", "offset", "order", "total"], page.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(attribute.GetRawText(), Assert.Single(page.GetProperty("items").EnumerateArray()).GetRawText());
        Assert.Equal(CollectionPagination.DefaultLimit, page.GetProperty("limit").GetInt32());
        Assert.Equal("[{\"field\":\"code\",\"direction\":\"asc\"}]", page.GetProperty("order").GetRawText());
        await using var context = await contexts.CreateDbContextAsync();
        Assert.Equal(name, (await context.ItemFamilies.AsNoTracking().SingleAsync(row => row.Code == Each)).Name);
    }

    [Fact]
    public async Task HttpPaginationPreservesTotalRequestedCapacityAndHugeOffsets()
    {
        await Error(await Send(HttpMethod.Post, ItemFamilyEndpoints.Path + "?unknown=1", "{\"code\":\"EA\",\"name\":\"Each\"}"),
            HttpStatusCode.BadRequest, ItemFamilyEndpoints.InvalidRequest);
        await Error(await Send(HttpMethod.Get, ItemFamilyEndpoints.Path + "?limit=" + (CollectionPagination.MaximumLimit + 1)),
            HttpStatusCode.BadRequest, ItemFamilyEndpoints.InvalidRequest);
        foreach (string code in OrderedCodes.Reverse()) await catalogue.CreateAsync(code, code);
        var page = await Body(await Send(HttpMethod.Get, ItemFamilyEndpoints.Path + "?offset=1&limit=1"));
        Assert.Equal(OrderedCodes.Length, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("offset").GetInt32());
        Assert.Equal(1, page.GetProperty("limit").GetInt32());
        Assert.Equal("A10", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty(ItemFamilyEndpoints.CodeField).GetString());
        var beyond = await Send(HttpMethod.Get, ItemFamilyEndpoints.Path + "?offset=9223372036854775808");
        Headers(beyond, HttpStatusCode.OK);
        Assert.Empty((await Body(beyond)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task CurrentIdentityAndAdminPrecedeDynamicGateWhichPrecedesBodyAndQueryValidation()
    {
        const string body = "{\"code\":\"EA\",\"name\":\"Each\"}";
        Headers(await Send(HttpMethod.Post, ItemFamilyEndpoints.Path, body), HttpStatusCode.Created);
        Headers(await Send(HttpMethod.Post, UomEndpoints.Path, JsonSerializer.Serialize(new { code = GateUom, description = "Preserved UoM" })), HttpStatusCode.Created);
        foreach (bool enabled in new[] { true, false })
        {
            await Toggle(enabled);
            foreach (string token in new[] { "", "invalid-fixture", userToken })
            {
                bool normal = token == userToken;
                await Error(await Send(HttpMethod.Post, ItemFamilyEndpoints.Path, "{", token),
                    normal ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, normal ? "forbidden" : "unauthorized");
                await Error(await Send(HttpMethod.Get, ItemFamilyEndpoints.Path + "?offset=bad", token: token),
                    normal ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, normal ? "forbidden" : "unauthorized");
            }
            if (enabled)
                await Error(await Send(HttpMethod.Post, ItemFamilyEndpoints.Path, "{"), HttpStatusCode.BadRequest, ItemFamilyEndpoints.InvalidRequest);
            else
            {
                await Error(await Send(HttpMethod.Get, UomEndpoints.Path), HttpStatusCode.ServiceUnavailable, ModuleDisabledResponse.ErrorCode);
                await Error(await Send(HttpMethod.Post, ItemFamilyEndpoints.Path, "{"), HttpStatusCode.ServiceUnavailable, ModuleDisabledResponse.ErrorCode);
                await Error(await Send(HttpMethod.Post, ItemFamilyEndpoints.Path, body), HttpStatusCode.ServiceUnavailable, ModuleDisabledResponse.ErrorCode);
                await Error(await Send(HttpMethod.Get, ItemFamilyEndpoints.Path + "?offset=bad"), HttpStatusCode.ServiceUnavailable, ModuleDisabledResponse.ErrorCode);
            }
        }
        await Toggle(true);
        var uoms = await Send(HttpMethod.Get, UomEndpoints.Path);
        Headers(uoms, HttpStatusCode.OK);
        Assert.Equal(GateUom, Assert.Single((await Body(uoms)).GetProperty("items").EnumerateArray()).GetProperty(UomEndpoints.CodeField).GetString());
        var page = await Send(HttpMethod.Get, ItemFamilyEndpoints.Path);
        Headers(page, HttpStatusCode.OK);
        Assert.Equal(Each, Assert.Single((await Body(page)).GetProperty("items").EnumerateArray()).GetProperty(ItemFamilyEndpoints.CodeField).GetString());
        Headers(await Send(HttpMethod.Post, ItemFamilyEndpoints.Path, "{\"code\":\"BOX\",\"name\":\"Box\"}"), HttpStatusCode.Created);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserAdministration>();
        await users.UpdateAsync(ownedUsers[0], false, null);
        await Error(await Send(HttpMethod.Get, ItemFamilyEndpoints.Path), HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task EmptyDefaultsAndHttpCodeAndUnicodeBoundaries()
    {
        var empty = await Send(HttpMethod.Get, ItemFamilyEndpoints.Path);
        Headers(empty, HttpStatusCode.OK);
        var page = await Body(empty);
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(0, page.GetProperty("total").GetInt32());
        Assert.Equal(0, page.GetProperty("offset").GetInt32());
        Assert.Equal(CollectionPagination.DefaultLimit, page.GetProperty("limit").GetInt32());
        foreach (string code in new[] { "1", "1A", "cold", "Cold", "_COLD", "COLD-", "COLD SPACE", " COLD", "COLD ", "COLD\n", "ABCDEFGHIJKLMNOPQRSTUVWXYZABCDE" })
            await Error(await Send(HttpMethod.Post, ItemFamilyEndpoints.Path, JsonSerializer.Serialize(new { code, name = "Cold" })), HttpStatusCode.BadRequest, ItemFamilyEndpoints.InvalidRequest);
        string maximum = string.Concat(Enumerable.Repeat("📦", ItemFamily.NameMaximum));
        await Error(await Send(HttpMethod.Post, ItemFamilyEndpoints.Path, JsonSerializer.Serialize(new { code = Each, name = maximum + "x" })), HttpStatusCode.BadRequest, ItemFamilyEndpoints.InvalidRequest);
        var created = await Send(HttpMethod.Post, ItemFamilyEndpoints.Path, JsonSerializer.Serialize(new { code = Each, name = " \t" + maximum + "\r\n " }));
        Headers(created, HttpStatusCode.Created);
        Assert.Equal(maximum, (await Body(created)).GetProperty(ItemFamilyEndpoints.NameField).GetString());
        Assert.Null(created.Headers.Location);
        foreach (string code in new[] { "A", "COLD", "COLD_CHAIN", "A_1", "ZONE2", "IS_FOOD", "ABCDEFGHIJKLMNOPQRSTUVWXYZABCD" })
        {
            try { Headers(await Send(HttpMethod.Post, ItemFamilyEndpoints.Path, JsonSerializer.Serialize(new { code, name = "Cold" })), HttpStatusCode.Created); }
            finally
            {
                await using var context = await contexts.CreateDbContextAsync();
                await context.ItemFamilies.Where(row => row.Code == code).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task OpenApiHasOnlyCreateListWithClosedShapesAndSharedPagination()
    {
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        Assert.True(paths.TryGetProperty(UomEndpoints.Path, out _));
        Assert.Equal(2, paths.EnumerateObject().Count(path => path.Name.StartsWith(ItemFamilyEndpoints.Path + "/", StringComparison.Ordinal)));
        var association = paths.GetProperty(ItemFamilyAssociationEndpoints.Path);
        Assert.Equal(["get", "patch", "post", "put"], association.EnumerateObject().Select(operation => operation.Name).Order());
        Assert.Equal("delete", Assert.Single(paths.GetProperty(ItemFamilyAssociationEndpoints.Path + "/{storageAttributeId}").EnumerateObject()).Name);
        var operations = paths.GetProperty(ItemFamilyEndpoints.Path);
        Assert.Equal(["get", "post"], operations.EnumerateObject().Select(operation => operation.Name).Order());
        JsonElement Schema(JsonElement message)
        {
            var schema = message.GetProperty("content").GetProperty("application/json").GetProperty("schema");
            return schema.TryGetProperty("$ref", out var reference)
                ? document.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1]) : schema;
        }
        void Closed(JsonElement schema, string[] fields)
        {
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(fields.Order(), schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order());
            Assert.Equal(fields.Order(), schema.GetProperty("required").EnumerateArray().Select(property => property.GetString()).Order());
        }
        var post = operations.GetProperty("post");
        Closed(Schema(post.GetProperty("requestBody")), ["code", "name"]);
        var response = Schema(post.GetProperty("responses").GetProperty("201"));
        Closed(response, ["id", "code", "name"]);
        Assert.Equal(ItemFamily.CodeMaximum, response.GetProperty("properties").GetProperty("code").GetProperty("maxLength").GetInt32());
        Assert.Equal(ItemFamily.NameMaximum, response.GetProperty("properties").GetProperty("name").GetProperty("maxLength").GetInt32());
        Assert.False(Schema(post.GetProperty("requestBody")).GetProperty("properties").GetProperty("name").TryGetProperty("maxLength", out _));
        var get = operations.GetProperty("get");
        Closed(Schema(get.GetProperty("responses").GetProperty("200")), ["items", "offset", "limit", "total", "order"]);
        Assert.Equal([CollectionPagination.LimitParameter, CollectionPagination.OffsetParameter], get.GetProperty("parameters").EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()).Order());
        foreach (var operation in new[] { get, post })
        {
            Assert.NotEmpty(operation.GetProperty("security").EnumerateArray());
            Assert.True(operation.GetProperty("responses").TryGetProperty("503", out _));
        }
    }

    private async Task Toggle(bool enabled)
    {
        var response = await Send(HttpMethod.Patch, ModuleEndpoints.Path + "/" + WmsModule.Id, JsonSerializer.Serialize(new { enabled }));
        Headers(response, HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? body = null, string? token = null, string? locale = null)
    {
        using var request = new HttpRequestMessage(method, path);
        string bearer = token ?? adminToken;
        if (bearer.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, bearer);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (locale is not null) request.Headers.AcceptLanguage.ParseAdd(locale);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>());

    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Headers(response, status);
        var body = await Body(response);
        ContractErrors.Single(body, code);
    }

    private static void Headers(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        if (status == HttpStatusCode.Unauthorized) Assert.Equal(BearerScheme, Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        else Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Null(response.Headers.RetryAfter);
    }
}
