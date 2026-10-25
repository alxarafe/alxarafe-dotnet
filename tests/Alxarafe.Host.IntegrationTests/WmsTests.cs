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
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class WmsTests(TenantApplicationFactory factory) : IClassFixture<TenantApplicationFactory>, IAsyncLifetime
{
    private const string Password = "wms-native-fixture-password";
    private const string LoginPath = "/api/auth/login";
    private const string BearerScheme = "Bearer";
    private const string Each = "EA";
    private const string Box = "BOX";
    private const string Race = "RACE";
    private static readonly string[] OrderedCodes = ["A1", "A10", "A2", "B"];
    private static readonly string[] OwnedCodes = [Each, Box, Race, .. OrderedCodes];
    private readonly List<Guid> ownedUsers = [];
    private HttpClient client = null!;
    private string adminToken = "";
    private string userToken = "";
    private UomCatalogue catalogue = null!;
    private IDbContextFactory<WmsDbContext> contexts = null!;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        contexts = factory.Services.GetRequiredService<IDbContextFactory<WmsDbContext>>();
        catalogue = factory.Services.GetRequiredService<UomCatalogue>();
        await factory.Services.GetRequiredService<ModuleStateService>().EnableAsync(WmsModule.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserAdministration>();
        foreach (bool admin in new[] { true, false })
        {
            var user = (await users.CreateAsync("native-wms-" + Guid.NewGuid().ToString("N") + "@example.test", Password, admin))!;
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
        await context.Uoms.Where(row => OwnedCodes.Contains(row.Code)).ExecuteDeleteAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(user => ownedUsers.Contains(user.Id)).ExecuteDeleteAsync();
        client.Dispose();
    }

    [Fact]
    public async Task WmsOwnsItsContextMigrationAndHistoryAlongsideUnchangedPlatform()
    {
        await using var context = await contexts.CreateDbContextAsync();
        Assert.Equal(["202610140001_Uoms", "202610150001_StorageAttributes", "202610160001_ItemFamilies", Alxarafe.Modules.Wms.Infrastructure.Migrations.InventoryFoundation.MigrationId], await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        await context.Database.MigrateAsync();
        var entity = context.Model.FindEntityType(typeof(UomRow))!;
        Assert.Equal(WmsStorage.UomTable, entity.GetTableName());
        Assert.Equal(["code", "description", "id"], entity.GetProperties().Select(property => property.GetColumnName()).Order(StringComparer.Ordinal));
        var index = Assert.Single(entity.GetIndexes());
        Assert.True(index.IsUnique);
        Assert.Equal(WmsStorage.UomCodeIndex, index.GetDatabaseName());
        await using var platform = await factory.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        Assert.Equal(["202610120001_ModuleStates", Alxarafe.EntityFrameworkCore.Migrations.TenantAiSettingsMigration.MigrationId, Alxarafe.EntityFrameworkCore.Migrations.AuthoritativeTenancy.MigrationId], await platform.Database.GetAppliedMigrationsAsync());
        Assert.DoesNotContain(platform.Model.GetEntityTypes(), type => type.ClrType == typeof(UomRow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Host=postgres;Database=alxarafe")]
    [InlineData("Host=postgres;Database=alxarafe_security_test")]
    public void TestingRejectsAnyUnapprovedOrMissingWmsConnection(string? connection)
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>();
        Assert.Throws<InvalidOperationException>(() => WmsStorage.Configure(options, connection, testing: true));
    }

    [Fact]
    public async Task EfPersistsNormalizedSupplementaryUnicodeAndOrdersBeforeSqlPagination()
    {
        foreach (string code in OrderedCodes.Reverse()) Assert.Equal(CreateUomOutcome.Created, (await catalogue.CreateAsync(code, code)).Outcome);
        string maximum = string.Concat(Enumerable.Repeat("📦", Uom.DescriptionMaximum));
        var created = (await catalogue.CreateAsync(Each, " \t" + maximum + "\r\n ")).Uom!;
        await using var context = await contexts.CreateDbContextAsync();
        var persisted = await context.Uoms.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        Assert.Equal(maximum, persisted.Description);
        Assert.NotEqual(Guid.Empty, persisted.Id);
        var page = await catalogue.ListAsync(BigInteger.One, 2);
        Assert.Equal(["A10", "A2"], page.Items.Select(uom => uom.Code));
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
        Assert.Single(results, result => result.Outcome == CreateUomOutcome.Created);
        Assert.Single(results, result => result.Outcome == CreateUomOutcome.CodeConflict);
        await using var context = await contexts.CreateDbContextAsync();
        var original = await context.Uoms.AsNoTracking().SingleAsync(row => row.Code == Race);
        Assert.Equal("Race", original.Description);
        Assert.Equal(CreateUomOutcome.CodeConflict, (await catalogue.CreateAsync(Race, "Changed")).Outcome);
        await context.Entry(original).ReloadAsync();
        Assert.Equal("Race", original.Description);
        context.Uoms.Add(new UomRow { Id = Guid.NewGuid(), Code = Race, Description = "Direct duplicate" });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    [Fact]
    public async Task DatabaseRequiresIdentityCodeAndDescription()
    {
        var uom = (await catalogue.CreateAsync(Each, "Each")).Uom!;
        foreach (var row in new[]
        {
            new UomRow { Id = Guid.NewGuid(), Code = null!, Description = "Box" },
            new UomRow { Id = Guid.NewGuid(), Code = Box, Description = null! },
            new UomRow { Id = uom.Id, Code = Box, Description = "Box" }
        })
        {
            await using var context = await contexts.CreateDbContextAsync();
            context.Uoms.Add(row);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("\"scalar\"")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"code\":\"EA\"}")]
    [InlineData("{\"description\":\"Each\"}")]
    [InlineData("{\"Code\":\"EA\",\"description\":\"Each\"}")]
    [InlineData("{\"code\":\"EA\",\"description\":\"Each\",\"extra\":true}")]
    [InlineData("{\"code\":\"EA\",\"code\":\"BOX\",\"description\":\"Each\"}")]
    [InlineData("{\"code\":\"EA\",\"description\":\"Each\",\"description\":\"Box\"}")]
    [InlineData("{\"code\":42,\"description\":\"Each\"}")]
    [InlineData("{\"code\":true,\"description\":\"Each\"}")]
    [InlineData("{\"code\":null,\"description\":\"Each\"}")]
    [InlineData("{\"code\":\"EA\",\"description\":42}")]
    [InlineData("{\"code\":\"EA\",\"description\":false}")]
    [InlineData("{\"code\":\"EA\",\"description\":null}")]
    [InlineData("{\"code\":\"EA\",\"description\":\"Each\"} {}")]
    [InlineData("{\"code\":\"EA\",\"description\":\"\\ud800\"}")]
    [InlineData("{\"code\":\"EA\",\"description\":\"\\udc00\"}")]
    [InlineData("{\"code\":\"EA\",\"description\":\"\\u0000\"}")]
    public async Task StrictJsonRejectsInvalidBodiesWithoutMutation(string input)
    {
        await Error(await Send(HttpMethod.Post, UomEndpoints.Path, input), HttpStatusCode.BadRequest, UomEndpoints.InvalidRequest);
        await using var context = await contexts.CreateDbContextAsync();
        Assert.False(await context.Uoms.AnyAsync(row => OwnedCodes.Contains(row.Code)));
    }

    [Theory]
    [InlineData("?offset=-1")]
    [InlineData("?offset=0.5")]
    [InlineData("?offset=1e2")]
    [InlineData("?offset=%200")]
    [InlineData("?offset=")]
    [InlineData("?limit=")]
    [InlineData("?limit=0")]
    [InlineData("?sort=code")]
    [InlineData("?offset=0&offset=1")]
    [InlineData("?limit=1&limit=1")]
    [InlineData("?Offset=1")]
    public async Task StrictQueryReusesCollectionsParserAndRejectsUnknownRepeatedOrInvalidValues(string query) =>
        await Error(await Send(HttpMethod.Get, UomEndpoints.Path + query), HttpStatusCode.BadRequest, UomEndpoints.InvalidRequest);

    [Fact]
    public async Task HttpCommitsNormalizedUserTextWithExactEnvelopeAndLocaleIndependentErrors()
    {
        const string description = "\u00a0Standard   📦\u00a0";
        var created = await Send(HttpMethod.Post, UomEndpoints.Path, JsonSerializer.Serialize(new { code = Each, description = " \t" + description + "\r\n " }));
        Headers(created, HttpStatusCode.Created);
        var uom = await Body(created);
        Assert.Equal(["code", "description", "id"], uom.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(description, uom.GetProperty(UomEndpoints.DescriptionField).GetString());
        Assert.NotEqual(Guid.Empty, uom.GetProperty("id").GetGuid());
        foreach (string locale in new[] { "en", "es" })
            await Error(await Send(HttpMethod.Post, UomEndpoints.Path, JsonSerializer.Serialize(new { code = Each, description = "Changed" }), locale: locale),
                HttpStatusCode.Conflict, UomEndpoints.CodeConflict);
        var listing = await Send(HttpMethod.Get, UomEndpoints.Path, locale: "es-ES");
        Headers(listing, HttpStatusCode.OK);
        var page = await Body(listing);
        Assert.Equal(["items", "limit", "offset", "order", "total"], page.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(uom.GetRawText(), Assert.Single(page.GetProperty("items").EnumerateArray()).GetRawText());
        Assert.Equal(CollectionPagination.DefaultLimit, page.GetProperty("limit").GetInt32());
        Assert.Equal("[{\"field\":\"code\",\"direction\":\"asc\"}]", page.GetProperty("order").GetRawText());
        await using var context = await contexts.CreateDbContextAsync();
        Assert.Equal(description, (await context.Uoms.AsNoTracking().SingleAsync(row => row.Code == Each)).Description);
    }

    [Fact]
    public async Task HttpPaginationPreservesTotalRequestedCapacityAndHugeOffsets()
    {
        await Error(await Send(HttpMethod.Post, UomEndpoints.Path + "?unknown=1", "{\"code\":\"EA\",\"description\":\"Each\"}"),
            HttpStatusCode.BadRequest, UomEndpoints.InvalidRequest);
        await Error(await Send(HttpMethod.Get, UomEndpoints.Path + "?limit=" + (CollectionPagination.MaximumLimit + 1)),
            HttpStatusCode.BadRequest, UomEndpoints.InvalidRequest);
        foreach (string code in OrderedCodes.Reverse()) await catalogue.CreateAsync(code, code);
        var page = await Body(await Send(HttpMethod.Get, UomEndpoints.Path + "?offset=1&limit=1"));
        Assert.Equal(OrderedCodes.Length, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("offset").GetInt32());
        Assert.Equal(1, page.GetProperty("limit").GetInt32());
        Assert.Equal("A10", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty(UomEndpoints.CodeField).GetString());
        var beyond = await Send(HttpMethod.Get, UomEndpoints.Path + "?offset=9223372036854775808");
        Headers(beyond, HttpStatusCode.OK);
        Assert.Empty((await Body(beyond)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task CurrentIdentityAndAdminPrecedeDynamicGateWhichPrecedesBodyAndQueryValidation()
    {
        const string body = "{\"code\":\"EA\",\"description\":\"Each\"}";
        Headers(await Send(HttpMethod.Post, UomEndpoints.Path, body), HttpStatusCode.Created);
        foreach (bool enabled in new[] { true, false })
        {
            await Toggle(enabled);
            foreach (string token in new[] { "", "invalid-fixture", userToken })
            {
                bool normal = token == userToken;
                await Error(await Send(HttpMethod.Post, UomEndpoints.Path, "{", token),
                    normal ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, normal ? "forbidden" : "unauthorized");
                await Error(await Send(HttpMethod.Get, UomEndpoints.Path + "?offset=bad", token: token),
                    normal ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, normal ? "forbidden" : "unauthorized");
            }
            if (!enabled)
            {
                await Error(await Send(HttpMethod.Post, UomEndpoints.Path, "{"), HttpStatusCode.ServiceUnavailable, ModuleDisabledResponse.ErrorCode);
                await Error(await Send(HttpMethod.Post, UomEndpoints.Path, body), HttpStatusCode.ServiceUnavailable, ModuleDisabledResponse.ErrorCode);
                await Error(await Send(HttpMethod.Get, UomEndpoints.Path + "?offset=bad"), HttpStatusCode.ServiceUnavailable, ModuleDisabledResponse.ErrorCode);
            }
        }
        await Toggle(true);
        var page = await Send(HttpMethod.Get, UomEndpoints.Path);
        Headers(page, HttpStatusCode.OK);
        Assert.Equal(Each, Assert.Single((await Body(page)).GetProperty("items").EnumerateArray()).GetProperty(UomEndpoints.CodeField).GetString());
        Headers(await Send(HttpMethod.Post, UomEndpoints.Path, "{\"code\":\"BOX\",\"description\":\"Box\"}"), HttpStatusCode.Created);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserAdministration>();
        await users.UpdateAsync(ownedUsers[0], false, null);
        await Error(await Send(HttpMethod.Get, UomEndpoints.Path), HttpStatusCode.Unauthorized, "unauthorized");
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
