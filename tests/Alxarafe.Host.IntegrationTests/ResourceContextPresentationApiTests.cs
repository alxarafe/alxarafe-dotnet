using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Alxarafe.Modules.Wms;
using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ResourceContextPresentationApiTests(ResourceApiTestContext context) : IClassFixture<ResourceApiTestContext>
{
    private const string ResourceId = "fixture.contextual", NameField = "name", TypeField = "typeId";
    private static ResourceFormDefinition Form(params string[] names) => new([new("general", [new("details", [new("identity", names)])])]);
    private static ResourceContextPresentationDefinition Contexts(string? label = null) => new(
        new([new(NameField, label), new(TypeField)]), Form(TypeField, NameField), Form("id", NameField, TypeField), Form(NameField, TypeField, "id"));
    private static ResourceDefinition Definition(ResourceContextPresentationDefinition? contextual) => new(ResourceId, "1.0.0", WmsModule.Id, new("id"),
        [new("id", ScalarType.Uuid, readOnly: true, immutable: true), new(NameField, ScalarType.String, required: true), new(TypeField, ScalarType.Uuid, immutable: true)],
        [ResourceOperation.Get, ResourceOperation.Update], new([new(ResourceOperation.Get, [ResourceApiFixtures.Read]), new(ResourceOperation.Update, [ResourceApiFixtures.Write])]),
        presentation: new(Form("id", NameField, TypeField)), contextualPresentation: contextual);
    private sealed class Provider(ResourceDefinition definition) : IResourceDefinitionProvider
    { public IReadOnlyCollection<ResourceDefinition> GetResourceDefinitions() => [definition]; }
    private WebApplicationFactory<Program> Host(ResourceDefinition definition) => context.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IResourceDefinitionProvider>(new Provider(definition))));
    private static string PathFor(string id = ResourceId) => ResourceEndpoints.MetadataPath + "/" + id + ResourceEndpoints.PresentationSuffix;
    private static async Task<HttpResponseMessage> Get(HttpClient client, string? token, string? etag = null, string? path = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path ?? PathFor());
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (etag is not null) request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, etag);
        return await client.SendAsync(request);
    }
    private static async Task<JsonElement> Body(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();
    private static string[] Fields(JsonElement form) => form.GetProperty("tabs").EnumerateArray().SelectMany(tab => tab.GetProperty("sections").EnumerateArray())
        .SelectMany(section => section.GetProperty("groups").EnumerateArray()).SelectMany(group => group.GetProperty("fields").EnumerateArray()).Select(field => field.GetString()!).ToArray();
    private static void Cache(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl!.Private); Assert.True(response.Headers.CacheControl.NoCache);
        Assert.False(response.Headers.CacheControl.Public);
        Assert.Equal([HeaderNames.Authorization], response.Headers.Vary);
        Assert.NotNull(response.Headers.ETag);
    }
    [Fact]
    public async Task ContextsHaveClosedSeparateShapesExactOrderAndSameDefinitionHashAsLegacyMetadata()
    {
        using var host = Host(Definition(Contexts("Display name"))); using var client = host.CreateClient();
        using var response = await Get(client, context.Writer); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Cache(response);
        var body = await Body(response); Assert.Equal(["definitionHash", "id", "presentation"], body.EnumerateObject().Select(p => p.Name).Order());
        var presentation = body.GetProperty("presentation"); Assert.Equal(["create", "edit", "list", "view"], presentation.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal([NameField, TypeField], presentation.GetProperty("list").GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("field").GetString()));
        Assert.Equal("Display name", presentation.GetProperty("list").GetProperty("columns")[0].GetProperty("label").GetString());
        Assert.False(presentation.GetProperty("list").GetProperty("columns")[1].TryGetProperty("label", out _));
        Assert.Equal([TypeField, NameField], Fields(presentation.GetProperty("create")));
        Assert.Equal(["id", NameField, TypeField], Fields(presentation.GetProperty("edit")));
        Assert.Equal([NameField, TypeField, "id"], Fields(presentation.GetProperty("view")));
        using var metadataRequest = new HttpRequestMessage(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + ResourceId);
        metadataRequest.Headers.Authorization = new("Bearer", context.Writer);
        using var metadataResponse = await client.SendAsync(metadataRequest); var metadata = await Body(metadataResponse);
        Assert.Equal(body.GetProperty("definitionHash").GetString(), metadata.GetProperty("definitionHash").GetString());
        Assert.Single(metadata.GetProperty("presentation").EnumerateObject()); Assert.True(metadata.GetProperty("presentation").TryGetProperty("form", out _));
        Assert.False(metadata.TryGetProperty("contextualPresentation", out _)); Assert.False(metadata.TryGetProperty("create", out _));
        Assert.True(metadata.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == TypeField).GetProperty("immutable").GetBoolean());
    }
    [Fact]
    public async Task LegacyFormIsNotMergedAndAbsentContextsDifferFromExplicitEmptyContexts()
    {
        using var host = Host(Definition(null)); using var client = host.CreateClient();
        using var legacy = await Get(client, context.Writer); Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Empty((await Body(legacy)).GetProperty("presentation").EnumerateObject());
        using var existing = await Get(client, context.Writer, path: PathFor(ResourceApiFixtures.Customer));
        Assert.Equal(HttpStatusCode.OK, existing.StatusCode); Assert.Empty((await Body(existing)).GetProperty("presentation").EnumerateObject());
        using var emptyHost = Host(Definition(new(list: new([]), edit: new([])))); using var emptyClient = emptyHost.CreateClient();
        using var empty = await Get(emptyClient, context.Writer); var presentation = (await Body(empty)).GetProperty("presentation");
        Assert.False(presentation.TryGetProperty("create", out _)); Assert.False(presentation.TryGetProperty("view", out _));
        Assert.Empty(presentation.GetProperty("edit").GetProperty("tabs").EnumerateArray());
        Assert.Empty(presentation.GetProperty("list").GetProperty("columns").EnumerateArray());
    }
    [Fact]
    public async Task StaticDiscoveryRequiresAuthenticationButNotCrudPermissionOrTenant()
    {
        using var host = Host(Definition(Contexts())); using var client = host.CreateClient();
        using var writer = await Get(client, context.Writer); var body = (await Body(writer)).GetProperty("presentation").GetRawText();
        foreach (string token in new[] { context.Reader, context.Denied, context.NoTenant, context.MultipleTenants })
        {
            using var response = await Get(client, token); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Cache(response);
            Assert.Equal(body, (await Body(response)).GetProperty("presentation").GetRawText());
        }
    }
    [Fact]
    public async Task ConditionalGetReturns304WithHeadersAndNoBodyAndAuthenticatesBeforeWildcard()
    {
        using var host = Host(Definition(Contexts())); using var client = host.CreateClient();
        using var initial = await Get(client, context.Writer); string etag = initial.Headers.ETag!.ToString();
        foreach (string condition in new[] { etag, "W/" + etag, "*" })
        {
            using var response = await Get(client, context.Writer, condition); Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
            Cache(response); Assert.Equal(etag, response.Headers.ETag!.ToString()); Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }
        using var anonymous = await Get(client, null, "*", PathFor("unknown") + "?unexpected=1");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode); Assert.Contains(anonymous.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
        Assert.Equal("unauthorized", (await Body(anonymous)).GetProperty("code").GetString());
        Assert.Equal("no-store", anonymous.Headers.CacheControl!.ToString());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueryIsRejectedBeforeResourceLookupEvenWithMatchingConditional(bool unknown)
    {
        using var host = Host(Definition(Contexts())); using var client = host.CreateClient();
        using var response = await Get(client, context.Denied, "*", PathFor(unknown ? "unknown" : ResourceId) + "?unexpected=1");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal("invalid_request", (await Body(response)).GetProperty("code").GetString());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }
    [Fact]
    public async Task UnknownResourceReturnsClosed404InsteadOf304()
    {
        using var response = await Get(context.Client, context.Writer, "*", PathFor("unknown"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); var body = await Body(response);
        Assert.Equal(["code", "errors"], body.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("resource_not_found", body.GetProperty("code").GetString());
    }
    [Fact]
    public async Task ContextChangeInvalidatesETagButLeavesLegacyWireShapeAndCrudOpenApiUnchanged()
    {
        using var firstHost = Host(Definition(Contexts("First"))); using var first = firstHost.CreateClient();
        using var secondHost = Host(Definition(Contexts("Second"))); using var second = secondHost.CreateClient();
        using var initial = await Get(first, context.Writer); string etag = initial.Headers.ETag!.ToString();
        using var changed = await Get(second, context.Writer, etag);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode); Assert.NotEqual(etag, changed.Headers.ETag!.ToString());
        var firstApi = await first.GetFromJsonAsync<JsonElement>("/openapi/v1.json"); var secondApi = await second.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.Equal(firstApi.GetProperty("paths").GetProperty(ResourceEndpoints.ResourcePath + "/" + ResourceId + "/{id}").GetRawText(),
            secondApi.GetProperty("paths").GetProperty(ResourceEndpoints.ResourcePath + "/" + ResourceId + "/{id}").GetRawText());
        async Task<JsonNode> Legacy(HttpClient client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + ResourceId);
            request.Headers.Authorization = new("Bearer", context.Writer); using var response = await client.SendAsync(request);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!; json.AsObject().Remove("definitionHash"); return json;
        }
        Assert.True(JsonNode.DeepEquals(await Legacy(first), await Legacy(second)));
    }
    [Fact]
    public async Task InvalidContextualDefinitionFailsHostInitialization()
    {
        using var host = Host(Definition(new(create: Form("unknown"))));
        var failure = await Assert.ThrowsAsync<ResourceValidationException>(async () => { using var client = host.CreateClient(); await client.GetAsync("/health"); });
        Assert.Equal(ResourceValidationCode.InvalidPresentation, failure.Code);
    }
    [Theory]
    [InlineData("Hidden")]
    [InlineData("FormExcluded")]
    public async Task ContextsRetainDeclarationsWithoutOverridingTypedStaticHints(string term)
    {
        var field = new FieldDefinition(NameField, ScalarType.String, immutable: true, annotations: [new("UI", term, new AnnotationBoolean(true))]);
        var definition = new ResourceDefinition(ResourceId, "1.0.0", WmsModule.Id, new("id"), [new("id", ScalarType.Uuid, readOnly: true, immutable: true), field], [], new([]),
            contextualPresentation: new(new([new(NameField)]), Form(NameField), Form(NameField), Form(NameField)));
        using var host = Host(definition); using var client = host.CreateClient();
        using var response = await Get(client, context.Denied); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var presentation = (await Body(response)).GetProperty("presentation");
        Assert.Equal([NameField], Fields(presentation.GetProperty("edit")));
        Assert.DoesNotContain("\"immutable\"", presentation.GetRawText(), StringComparison.Ordinal);
        using var request = new HttpRequestMessage(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + ResourceId);
        request.Headers.Authorization = new("Bearer", context.Denied); using var metadata = await client.SendAsync(request);
        var projected = (await Body(metadata)).GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == NameField);
        Assert.True(projected.GetProperty("immutable").GetBoolean());
        Assert.True(projected.GetProperty("annotations").EnumerateArray().Single(a => a.GetProperty("term").GetString() == term).GetProperty("value").GetBoolean());
    }

}
