using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.Resources.Runtime;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ResourcePublicProtocolTests : IClassFixture<ResourceApiTestContext>
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static readonly string[] ErrorDetailFields = ["code", "resource", "resourceId", "path"];
    private readonly ResourceApiTestContext context;
    public ResourcePublicProtocolTests(ResourceApiTestContext context) { this.context = context; context.Reset(); }
    private static async Task<JsonElement> Body(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public void GenericPageOmitsUnknownTotalWithoutReusingLegacyOrderEnvelope()
    {
        var page = JsonSerializer.SerializeToElement(new ResourcePageDto([], 0, ResourceQuery.DefaultLimit, null), WireJson);
        Assert.Equal(["items", "limit", "offset"], page.EnumerateObject().Select(property => property.Name).Order());
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.False(page.TryGetProperty("total", out _)); Assert.False(page.TryGetProperty("order", out _));
    }

    [Fact]
    public async Task GeneratedCrudDocuments409SeparatePageAndTheProvenQueryProtocol()
    {
        using var response = await context.Send(HttpMethod.Get, "/openapi/v1.json");
        var document = await Body(response); var paths = document.GetProperty("paths");
        string root = ResourceApiTestContext.Path();
        foreach (var (path, method) in new[] { (root, "get"), (root, "post"), (root + "/{id}", "get"), (root + "/{id}", "patch"),
            (ResourceApiTestContext.Path(ResourceApiFixtures.Country) + "/{id}", "delete") })
            Assert.True(paths.GetProperty(path).GetProperty(method).GetProperty("responses").TryGetProperty("409", out _));
        var list = paths.GetProperty(root).GetProperty("get");
        Assert.Equal(["offset", "limit", "search", "sort", "filter"], list.GetProperty("parameters").EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()));
        var page = list.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal(["items", "limit", "offset"], page.GetProperty("required").EnumerateArray().Select(value => value.GetString()).Order());
        Assert.False(page.GetProperty("additionalProperties").GetBoolean()); Assert.False(page.GetProperty("properties").TryGetProperty("order", out _));
        Assert.True(page.GetProperty("properties").TryGetProperty("total", out _));
        Assert.False(paths.GetProperty(root + "/{id}").TryGetProperty("put", out _));
        var patch = paths.GetProperty(root + "/{id}").GetProperty("patch").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal("object", patch.GetProperty("type").GetString());
    }

    [Fact]
    public async Task MetadataPublishesTypedOperatorsWithoutExposingPermissionRules()
    {
        using var response = await context.Send(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + ResourceApiFixtures.Customer, token: context.Writer);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var metadata = await Body(response);
        foreach (var field in metadata.GetProperty("fields").EnumerateArray())
        {
            var capability = field.GetProperty("capabilities");
            var actual = capability.GetProperty("filterOperators").EnumerateArray().Select(value => value.GetString()).ToArray();
            string[] expected = !capability.GetProperty("filterable").GetBoolean() ? [] : field.GetProperty("type").GetString() switch
            {
                "STRING" or "TEXT" => ["eq", "ne", "lt", "lte", "gt", "gte", "contains", "startsWith"],
                "UINT8" => ["eq", "ne", "lt", "lte", "gt", "gte"],
                _ => ["eq", "ne"]
            };
            Assert.Equal(expected, actual);
            foreach (string name in new[] { "nullable", "required", "readOnly", "immutable", "constraints", "enumValues", "annotations" }) Assert.True(field.TryGetProperty(name, out _));
        }
        Assert.False(metadata.TryGetProperty("security", out _));
        Assert.All(metadata.GetProperty("operations").EnumerateObject(), operation => Assert.Equal(["authorized", "declared"], operation.Value.EnumerateObject().Select(value => value.Name).Order()));
        Assert.Equal("MANY_TO_ONE", Assert.Single(metadata.GetProperty("relations").EnumerateArray()).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task GenericPrecedenceExceptionAndCommonErrorShapeRemainObservable()
    {
        using var anonymous = await context.Send(HttpMethod.Post, ResourceEndpoints.ResourcePath + "/missing", "{", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var missing = await context.Send(HttpMethod.Get, ResourceEndpoints.ResourcePath + "/missing", token: context.Denied);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var malformed = await context.Send(HttpMethod.Post, ResourceApiTestContext.Path(), "{", context.Denied);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var forbidden = await context.Send(HttpMethod.Get, ResourceApiTestContext.Path(), token: context.Denied);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        foreach (var response in new[] { anonymous, missing, malformed, forbidden })
        {
            var body = await Body(response); Assert.Equal(["code", "errors"], body.EnumerateObject().Select(property => property.Name).Order());
            Assert.NotEmpty(body.GetProperty("errors").EnumerateArray());
            Assert.All(body.GetProperty("errors").EnumerateArray(), error => Assert.All(error.EnumerateObject(), property => Assert.Contains(property.Name, ErrorDetailFields)));
        }
    }
}
