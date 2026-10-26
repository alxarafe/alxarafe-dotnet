using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms;
using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.Resources.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ResourceApiTests : IClassFixture<ResourceApiTestContext>
{
    private readonly ResourceApiTestContext context;
    public ResourceApiTests(ResourceApiTestContext context) { this.context = context; context.Reset(); }
    private static string Meta(string resource = ResourceApiFixtures.Customer) => ResourceEndpoints.MetadataPath + "/" + resource;
    private static async Task<JsonElement> Body(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();
    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response) { Assert.Equal(status, response.StatusCode); var body = await Body(response); Assert.Equal(code, body.GetProperty("code").GetString()); Assert.NotEmpty(body.GetProperty("errors").EnumerateArray()); }
    }

    [Theory]
    [InlineData(ResourceOperation.List)]
    [InlineData(ResourceOperation.Get)]
    [InlineData(ResourceOperation.Create)]
    [InlineData(ResourceOperation.Update)]
    [InlineData(ResourceOperation.Delete)]
    public async Task UnknownResourceNeverFallsBackToAConcreteRepository(ResourceOperation operation)
    {
        var method = operation switch
        {
            ResourceOperation.List or ResourceOperation.Get => HttpMethod.Get,
            ResourceOperation.Create => HttpMethod.Post,
            ResourceOperation.Update => HttpMethod.Patch,
            _ => HttpMethod.Delete
        };
        bool item = operation is ResourceOperation.Get or ResourceOperation.Update or ResourceOperation.Delete;
        string path = ResourceApiTestContext.Path("missing-resource", item ? ResourceApiFixtures.FirstId : null);
        await Error(await context.Send(method, path, token: context.Writer), HttpStatusCode.NotFound, ResourceEndpoints.NotFound);
        Assert.Equal(0, context.Store.Calls);
    }

    [Theory]
    [InlineData("-1", false)]
    [InlineData("0", true)]
    [InlineData("255", true)]
    [InlineData("256", false)]
    public async Task HttpCreateAndPatchPreserveUInt8DomainWithoutControllerRangeRules(string number, bool valid)
    {
        string json = "{\"code\":\"BYTE\",\"name\":\"Boundary\",\"byteValue\":" + number + "}";
        if (valid)
        {
            using var created = await context.Send(HttpMethod.Post, ResourceApiTestContext.Path(), json, context.Writer);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal(number, (await Body(created)).GetProperty("byteValue").GetRawText());
            using var patched = await context.Send(HttpMethod.Patch, created.Headers.Location!.OriginalString, "{\"byteValue\":" + number + "}", context.Writer);
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
            Assert.Equal(number, (await Body(patched)).GetProperty("byteValue").GetRawText());
        }
        else
        {
            await Error(await context.Send(HttpMethod.Post, ResourceApiTestContext.Path(), json, context.Writer), HttpStatusCode.BadRequest, ResourceEndpoints.InvalidRequest);
            await Error(await context.Send(HttpMethod.Patch, ResourceApiTestContext.Path(id: ResourceApiFixtures.FirstId), "{\"byteValue\":" + number + "}", context.Writer), HttpStatusCode.BadRequest, ResourceEndpoints.InvalidRequest);
            Assert.Equal(0, context.Store.Calls);
        }
    }

    [Theory]
    [InlineData("{\"code\":\"NEW\",\"name\":\"\\uD800\"}")]
    [InlineData("{\"code\":\"NEW\",\"name\":\"Valid\",\"\\uDC00\":true}")]
    public async Task MalformedUnicodeBodyReturns400WithoutCallingPersistence(string body)
    {
        await Error(await context.Send(HttpMethod.Post, ResourceApiTestContext.Path(), body, context.Writer), HttpStatusCode.BadRequest, ResourceEndpoints.InvalidRequest);
        Assert.Equal(0, context.Store.Calls);
    }

    [Fact]
    public async Task MalformedUnicodeFilterIsRejectedWithoutRepositoryAccess()
    {
        string filter = Uri.EscapeDataString("""{"field":"name","operator":"eq","value":"\uD800"}""");
        await Error(await context.Send(HttpMethod.Get, ResourceApiTestContext.Path() + "?filter=" + filter, token: context.Reader), HttpStatusCode.BadRequest, ResourceEndpoints.InvalidRequest);
        Assert.Equal(0, context.Store.Calls);
    }

    [Fact]
    public async Task CrudPreservesDefaultsExplicitNullPartialPatchAndLocation()
    {
        using var created = await context.Send(HttpMethod.Post, ResourceApiTestContext.Path(), """{"code":"NEW","name":"New customer","optional":null,"fixed":"initial"}""", context.Writer);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("no-store", created.Headers.CacheControl!.ToString());
        var record = await Body(created); Assert.True(record.GetProperty("active").GetBoolean()); Assert.Equal(JsonValueKind.Null, record.GetProperty("optional").ValueKind);
        string location = created.Headers.Location!.OriginalString;
        using var get = await context.Send(HttpMethod.Get, location, token: context.Reader); Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var patch = await context.Send(HttpMethod.Patch, location, """{"name":"Changed","active":false}""", context.Writer);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode); var changed = await Body(patch); Assert.Equal("NEW", changed.GetProperty("code").GetString()); Assert.False(changed.GetProperty("active").GetBoolean()); Assert.Equal(JsonValueKind.Null, changed.GetProperty("optional").ValueKind);
        using var omitted = await context.Send(HttpMethod.Post, ResourceApiTestContext.Path(), """{"code":"NEXT","name":"Omitted"}""", context.Writer);
        Assert.Equal("fallback", (await Body(omitted)).GetProperty("optional").GetString());
        using var explicitNull = await context.Send(HttpMethod.Patch, omitted.Headers.Location!.OriginalString, """{"optional":null}""", context.Writer);
        Assert.Equal(JsonValueKind.Null, (await Body(explicitNull)).GetProperty("optional").ValueKind);
    }

    [Theory]
    [InlineData("signed", "-9223372036854775808", "-9223372036854775808")]
    [InlineData("unsigned", "4294967295", "4294967295")]
    [InlineData("amount", "1234567890123456789012345678901234567890.12345678901234567890", "1234567890123456789012345678901234567890.1234567890123456789")]
    [InlineData("rating", "1e1", "10")]
    [InlineData("active", "false", "false")]
    [InlineData("state", "\"open\"", "\"open\"")]
    [InlineData("date", "\"2026-10-25\"", "\"2026-10-25\"")]
    [InlineData("time", "\"12:34:56.1234567\"", "\"12:34:56.1234567\"")]
    [InlineData("instant", "\"2026-10-25T10:00:00+02:00\"", "\"2026-10-25T08:00:00.0000000Z\"")]
    [InlineData("binary", "\"AA==\"", "\"AA==\"")]
    [InlineData("countryId", "\"035a2161-c936-42dc-8b4b-d3cf5d1953a6\"", "\"035a2161-c936-42dc-8b4b-d3cf5d1953a6\"")]
    public async Task SemanticJsonValuesRoundTripExactly(string field, string input, string expected)
    {
        string json = "{\"code\":\"NEW\",\"name\":\"Customer\",\"" + field + "\":" + input + "}";
        using var response = await context.Send(HttpMethod.Post, ResourceApiTestContext.Path(), json, context.Writer);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.Equal(expected, (await Body(response)).GetProperty(field).GetRawText());
    }

    [Theory]
    [InlineData("\"rating\":\"3\"")]
    [InlineData("\"active\":\"true\"")]
    [InlineData("\"date\":\"2026-02-30\"")]
    [InlineData("\"instant\":\"2026-10-25T10:00:00\"")]
    [InlineData("\"time\":\"99:00:00\"")]
    [InlineData("\"binary\":\"invalid!\"")]
    [InlineData("\"countryId\":\"not-uuid\"")]
    [InlineData("\"amount\":1e999999999")]
    [InlineData("\"code\":\"DUPLICATE\"")]
    [InlineData("\"rating\":300")]
    [InlineData("\"state\":\"unknown\"")]
    [InlineData("\"permissions\":[\"wms.items.write\"]")]
    public async Task InvalidTransportOrRuntimeValuesNeverReachPersistence(string extra)
    {
        int calls = context.Store.Calls;
        await Error(await context.Send(HttpMethod.Post, ResourceApiTestContext.Path(), "{\"code\":\"NEW\",\"name\":\"Customer\"," + extra + "}", context.Writer), HttpStatusCode.BadRequest, ResourceEndpoints.InvalidRequest);
        Assert.Equal(calls, context.Store.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"id\":\"035a2161-c936-42dc-8b4b-d3cf5d1953a6\"}")]
    [InlineData("{\"active\":null}")]
    [InlineData("{\"fixed\":true}")]
    public async Task InvalidPatchIsRejectedWithoutRepositoryRead(string json)
    {
        await Error(await context.Send(HttpMethod.Patch, ResourceApiTestContext.Path(id: ResourceApiFixtures.FirstId), json, context.Writer), HttpStatusCode.BadRequest, ResourceEndpoints.InvalidRequest);
        Assert.Equal(0, context.Store.Calls);
    }

    [Fact]
    public async Task ImmutableMutationIsAConflictBeforeRepositoryAccess()
    {
        using var response = await context.Send(HttpMethod.Patch, ResourceApiTestContext.Path(id: ResourceApiFixtures.FirstId), "{\"fixed\":\"changed\"}", context.Writer);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await Body(response);
        Assert.Equal(ResourceEndpoints.Conflict, body.GetProperty("code").GetString());
        var detail = body.GetProperty("errors")[0];
        Assert.Equal(ResourceConflictException.ImmutableField, detail.GetProperty("code").GetString());
        Assert.Equal("/fixed", detail.GetProperty("path").GetString());
        Assert.Equal(0, context.Store.Calls);
    }

    [Fact]
    public async Task ExplicitDomainConflictUsesCommonEnvelopeAndPreservesState()
    {
        using var response = await context.Send(HttpMethod.Patch, ResourceApiTestContext.Path(id: ResourceApiFixtures.FirstId), JsonSerializer.Serialize(new { name = ResourceApiFixtures.LockedName }), context.Writer);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await Body(response);
        Assert.Equal(ResourceEndpoints.Conflict, body.GetProperty("code").GetString());
        Assert.Equal(ResourceApiFixtures.ConflictReason, body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(0, context.Store.Mutations);
        using var read = await context.Send(HttpMethod.Get, ResourceApiTestContext.Path(id: ResourceApiFixtures.FirstId), token: context.Reader);
        Assert.Equal("Water", (await Body(read)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task ListQueryFiltersSortsSearchesAndPaginates()
    {
        context.Seed(ResourceApiFixtures.Customer, Guid.NewGuid(), "C-2", "Water two");
        string filter = Uri.EscapeDataString("""{"field":"active","operator":"eq","value":true}""");
        using var response = await context.Send(HttpMethod.Get, ResourceApiTestContext.Path() + "?offset=0&limit=1&search=Water&sort=-name&sort=code&filter=" + filter, token: context.Reader);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var page = await Body(response);
        Assert.Equal("Water two", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("name").GetString()); Assert.Equal(2, page.GetProperty("total").GetInt64()); Assert.Equal(1, page.GetProperty("limit").GetInt32());
    }

    [Theory]
    [InlineData("sort=unknown")]
    [InlineData("sort=fixed")]
    [InlineData("limit=101")]
    [InlineData("limit=0")]
    [InlineData("offset=-1")]
    [InlineData("offset=0&offset=1")]
    [InlineData("tenantId=forged")]
    [InlineData("filter=%7B%22field%22%3A%22fixed%22%2C%22operator%22%3A%22eq%22%2C%22value%22%3A%22x%22%7D")]
    public async Task InvalidQueriesDoNotReachPersistence(string query)
    { await Error(await context.Send(HttpMethod.Get, ResourceApiTestContext.Path() + "?" + query, token: context.Reader), HttpStatusCode.BadRequest, ResourceEndpoints.InvalidRequest); Assert.Equal(0, context.Store.Calls); }

    [Fact]
    public async Task TypedIdentifiersNotFoundAndDeleteBehaviorUseExistingErrors()
    {
        await Error(await context.Send(HttpMethod.Get, ResourceApiTestContext.Path() + "/invalid-uuid", token: context.Reader), HttpStatusCode.BadRequest, ResourceEndpoints.InvalidRequest);
        await Error(await context.Send(HttpMethod.Get, ResourceApiTestContext.Path(id: Guid.NewGuid()), token: context.Reader), HttpStatusCode.NotFound, ResourceEndpoints.NotFound);
        int before = context.Store.Calls;
        await Error(await context.Send(HttpMethod.Delete, ResourceApiTestContext.Path(id: ResourceApiFixtures.FirstId), token: context.Writer), HttpStatusCode.MethodNotAllowed, ResourceEndpoints.NotSupported);
        Assert.Equal(before, context.Store.Calls);
        await Error(await context.Send(HttpMethod.Delete, ResourceApiTestContext.Path(ResourceApiFixtures.Country, ResourceApiFixtures.FirstId), token: context.Writer), HttpStatusCode.BadRequest, ResourceEndpoints.InvalidRequest);
        Guid removable = Guid.NewGuid(); context.Seed(ResourceApiFixtures.Country, removable, "REMOVE", "Remove");
        using var deleted = await context.Send(HttpMethod.Delete, ResourceApiTestContext.Path(ResourceApiFixtures.Country, removable), token: context.Writer);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode); Assert.Empty(await deleted.Content.ReadAsByteArrayAsync());
        await Error(await context.Send(HttpMethod.Delete, ResourceApiTestContext.Path(ResourceApiFixtures.Country, removable), token: context.Writer), HttpStatusCode.NotFound, ResourceEndpoints.NotFound);
    }

    [Fact]
    public async Task ExistingAuthenticationPermissionsAndTenantAuthorityRemainRequired()
    {
        await Error(await context.Send(HttpMethod.Get, ResourceApiTestContext.Path(id: ResourceApiFixtures.FirstId)), HttpStatusCode.Unauthorized, "unauthorized");
        await Error(await context.Send(HttpMethod.Get, ResourceApiTestContext.Path(id: ResourceApiFixtures.FirstId), token: context.Denied), HttpStatusCode.Forbidden, "forbidden");
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Patch, HttpMethod.Delete })
        {
            string path = method == HttpMethod.Post ? ResourceApiTestContext.Path() : ResourceApiTestContext.Path(ResourceApiFixtures.Country, ResourceApiFixtures.FirstId);
            await Error(await context.Send(method, path, method == HttpMethod.Delete ? null : """{"code":"NEW","name":"Forged","permissions":["wms.items.write"],"roles":["admin"]}""", context.Reader), HttpStatusCode.Forbidden, "forbidden");
        }
        await Error(await context.Send(HttpMethod.Get, ResourceApiTestContext.Path(id: ResourceApiFixtures.FirstId), token: context.NoTenant, spoofTenant: true), HttpStatusCode.Forbidden, TenantAccessException.Required);
        await Error(await context.Send(HttpMethod.Get, ResourceApiTestContext.Path(id: ResourceApiFixtures.FirstId), token: context.MultipleTenants, spoofTenant: true), HttpStatusCode.Forbidden, TenantAccessException.SelectionRequired);
        Assert.Equal(0, context.Store.Calls);
    }

    [Fact]
    public async Task MembershipRevocationIsEffectiveAndNoHeaderCanReplaceIt()
    {
        using var scope = context.Host.Services.CreateScope();
        using var platform = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        var memberships = await platform.TenantMemberships.Where(row => row.UserId == context.WriterId && row.Active).ToArrayAsync();
        try
        {
            await platform.TenantMemberships.Where(row => row.UserId == context.WriterId).ExecuteUpdateAsync(update => update.SetProperty(row => row.Active, false));
            await Error(await context.Send(HttpMethod.Get, ResourceApiTestContext.Path(id: ResourceApiFixtures.FirstId), token: context.Writer, spoofTenant: true), HttpStatusCode.Forbidden, TenantAccessException.Required);
            Assert.Equal(0, context.Store.Calls);
        }
        finally
        {
            foreach (var membership in memberships) await platform.TenantMemberships.Where(row => row.UserId == membership.UserId && row.TenantId == membership.TenantId).ExecuteUpdateAsync(update => update.SetProperty(row => row.Active, true));
        }
    }

    [Fact]
    public async Task MetadataProjectionIsExplicitCompleteAndDoesNotGrantAccess()
    {
        using var discovery = await context.Send(HttpMethod.Get, ResourceEndpoints.MetadataPath, token: context.Denied);
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode); var ids = (await Body(discovery)).EnumerateArray().Select(entry => entry.GetProperty("id").GetString()).ToArray(); Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
        using var response = await context.Send(HttpMethod.Get, Meta(), token: context.Denied); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var meta = await Body(response); Assert.Equal(context.Host.Services.GetRequiredService<ResourceRegistry>().DefinitionHashes[ResourceApiFixtures.Customer], meta.GetProperty("definitionHash").GetString());
        Assert.Equal("id", meta.GetProperty("idDefinition").GetProperty("field").GetString());
        Assert.False(meta.GetProperty("operations").GetProperty("create").GetProperty("authorized").GetBoolean()); Assert.False(meta.GetProperty("operations").GetProperty("delete").GetProperty("declared").GetBoolean());
        var rating = meta.GetProperty("fields").EnumerateArray().Single(field => field.GetProperty("name").GetString() == "rating");
        Assert.Equal("UINT8", rating.GetProperty("type").GetString()); Assert.Equal(0, rating.GetProperty("constraints").GetProperty("minimum").GetInt32()); Assert.Equal(10, rating.GetProperty("constraints").GetProperty("maximum").GetInt32());
        Assert.Equal(ResourceApiFixtures.Country, Assert.Single(meta.GetProperty("relations").EnumerateArray()).GetProperty("targetResource").GetString());
        var action = Assert.Single(meta.GetProperty("actions").EnumerateArray()); Assert.False(action.GetProperty("authorized").GetBoolean()); Assert.False(action.TryGetProperty("available", out _));
        Assert.NotEmpty(meta.GetProperty("annotations").EnumerateArray());
        string raw = meta.GetRawText(); foreach (string secret in new[] { "DbContext", "databaseReference", "Alxarafe.", "System.", ResourceApiFixtures.Write, "permissions", "claims" }) Assert.DoesNotContain(secret, raw, StringComparison.Ordinal);
        await Error(await context.Send(HttpMethod.Get, ResourceApiTestContext.Path(id: ResourceApiFixtures.FirstId), token: context.Denied), HttpStatusCode.Forbidden, "forbidden"); Assert.Equal(0, context.Store.Calls);
        await Error(await context.Send(HttpMethod.Get, Meta("unknown"), token: context.Writer), HttpStatusCode.NotFound, ResourceEndpoints.NotFound);
    }

    [Fact]
    public async Task ETagRevalidatesAuthorizationAndModuleAvailability()
    {
        using var first = await context.Send(HttpMethod.Get, Meta(), token: context.Writer); string tag = first.Headers.ETag!.ToString();
        using var same = await context.Send(HttpMethod.Get, Meta(), token: context.Writer, etag: tag); Assert.Equal(HttpStatusCode.NotModified, same.StatusCode); Assert.Empty(await same.Content.ReadAsByteArrayAsync());
        using var other = await context.Send(HttpMethod.Get, Meta(), token: context.Writer, etag: "\"different\""); Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        try
        {
            await context.SetWriterPermission(false);
            using var revoked = await context.Send(HttpMethod.Get, Meta(), token: context.Writer, etag: tag); Assert.Equal(HttpStatusCode.OK, revoked.StatusCode); Assert.NotEqual(tag, revoked.Headers.ETag!.ToString());
            Assert.False((await Body(revoked)).GetProperty("operations").GetProperty("create").GetProperty("authorized").GetBoolean());
        }
        finally { await context.SetWriterPermission(true); }
        var states = context.Host.Services.GetRequiredService<ModuleStateService>();
        try
        {
            await states.DisableAsync(WmsModule.Id);
            using var discovery = await context.Send(HttpMethod.Get, ResourceEndpoints.MetadataPath, token: context.Writer); Assert.DoesNotContain((await Body(discovery)).EnumerateArray(), resource => resource.GetProperty("moduleId").GetString() == WmsModule.Id);
            using var meta = await context.Send(HttpMethod.Get, Meta(), token: context.Writer, etag: tag); Assert.Equal(HttpStatusCode.OK, meta.StatusCode); Assert.False((await Body(meta)).GetProperty("moduleEnabled").GetBoolean());
            await Error(await context.Send(HttpMethod.Get, ResourceApiTestContext.Path(id: ResourceApiFixtures.FirstId), token: context.Writer), HttpStatusCode.ServiceUnavailable, "module_disabled"); Assert.Equal(0, context.Store.Calls);
        }
        finally { await states.EnableAsync(WmsModule.Id); }
    }

    [Fact]
    public async Task OpenApiDerivesConcreteRoutesSchemasAndPatchFromFrozenDefinitions()
    {
        using var response = await context.Client.GetAsync("/openapi/v1.json"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = (await Body(response)).GetProperty("paths"); string collection = ResourceApiTestContext.Path();
        Assert.False(paths.TryGetProperty("/api/resources/{resource}", out _));
        var item = paths.GetProperty(collection + "/{id}"); Assert.False(item.TryGetProperty("delete", out _));
        Assert.True(paths.GetProperty(ResourceApiTestContext.Path(ResourceApiFixtures.Country) + "/{id}").TryGetProperty("delete", out _));
        var schema = paths.GetProperty(collection).GetProperty("post").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var fields = schema.GetProperty("properties"); Assert.False(fields.TryGetProperty("id", out _));
        Assert.Equal("integer", fields.GetProperty("rating").GetProperty("type").GetString()); Assert.Equal(10, fields.GetProperty("rating").GetProperty("maximum").GetInt32());
        Assert.Equal(30, fields.GetProperty("code").GetProperty("maxLength").GetInt32()); Assert.Equal("^[A-Z0-9-]+$", fields.GetProperty("code").GetProperty("pattern").GetString());
        Assert.Equal("uuid", fields.GetProperty("countryId").GetProperty("format").GetString()); Assert.Equal("date", fields.GetProperty("date").GetProperty("format").GetString());
        Assert.Equal("date-time", fields.GetProperty("instant").GetProperty("format").GetString()); Assert.Equal(2, fields.GetProperty("state").GetProperty("enum").GetArrayLength());
        Assert.Contains(schema.GetProperty("required").EnumerateArray(), property => property.GetString() == "code");
        var patch = item.GetProperty("patch").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.False(patch.GetProperty("properties").TryGetProperty("fixed", out _)); Assert.False(patch.TryGetProperty("required", out var required) && required.GetArrayLength() > 0);
        Assert.Equal("uuid", item.GetProperty("get").GetProperty("parameters")[0].GetProperty("schema").GetProperty("format").GetString());
        Assert.True(paths.TryGetProperty(ResourceEndpoints.MetadataPath, out _)); Assert.True(paths.TryGetProperty(ResourceEndpoints.MetadataPath + "/{resource}", out _));
        Assert.Equal(0, context.Store.Calls);
    }
}
