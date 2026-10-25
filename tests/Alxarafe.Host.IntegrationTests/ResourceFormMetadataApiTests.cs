using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Modules.Wms;
using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.ResourceTestFixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ResourceFormMetadataApiTests(ResourceApiTestContext context) : IClassFixture<ResourceApiTestContext>
{
    private sealed class Provider(ResourceDefinition definition) : IResourceDefinitionProvider
    { public IReadOnlyCollection<ResourceDefinition> GetResourceDefinitions() => [definition]; }
    private WebApplicationFactory<Program> Host(ResourceDefinition definition) => context.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IResourceDefinitionProvider>(new Provider(definition))));
    private async Task<HttpResponseMessage> Metadata(HttpClient client, string? etag = null, string resourceId = ComplexFormFixture.Id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + resourceId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.Writer);
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        return await client.SendAsync(request);
    }
    private static async Task<JsonElement> Body(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();
    [Fact]
    public async Task CompleteFiveTabMetadataPreservesOrderReferencesAndNeutralBehavior()
    {
        var definition = ComplexFormFixture.Create(WmsModule.Id);
        using var host = Host(definition); using var client = host.CreateClient();
        using var response = await Metadata(client); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response); var form = body.GetProperty("presentation").GetProperty("form");
        Assert.Equal("CONFIRM_DISCARD", form.GetProperty("behavior").GetProperty("unsavedChanges").GetString());
        Assert.Equal(ComplexFormFixture.Duplicate, Assert.Single(form.GetProperty("actions").EnumerateArray()).GetString());
        Assert.Equal(5, form.GetProperty("tabs").GetArrayLength());
        for (int t = 0; t < definition.Presentation!.Form.Tabs.Count; t++)
        {
            var expectedTab = definition.Presentation.Form.Tabs[t]; var tab = form.GetProperty("tabs")[t];
            Assert.Equal(expectedTab.Id, tab.GetProperty("id").GetString()); Assert.Equal(expectedTab.Label, tab.GetProperty("label").GetString());
            Assert.Equal(expectedTab.Actions, tab.GetProperty("actions").EnumerateArray().Select(value => value.GetString()));
            for (int s = 0; s < expectedTab.Sections.Count; s++)
            {
                var expectedSection = expectedTab.Sections[s]; var section = tab.GetProperty("sections")[s];
                Assert.Equal(expectedSection.Id, section.GetProperty("id").GetString());
                Assert.Equal(expectedSection.Actions, section.GetProperty("actions").EnumerateArray().Select(value => value.GetString()));
                Assert.Equal(expectedSection.Collapsible, section.GetProperty("collapsible").GetBoolean()); Assert.Equal(expectedSection.InitiallyCollapsed, section.GetProperty("initiallyCollapsed").GetBoolean());
                for (int g = 0; g < expectedSection.Groups.Count; g++)
                {
                    var expectedGroup = expectedSection.Groups[g]; var group = section.GetProperty("groups")[g];
                    Assert.Equal(expectedGroup.Id, group.GetProperty("id").GetString()); Assert.Equal(expectedGroup.Fields, group.GetProperty("fields").EnumerateArray().Select(value => value.GetString()));
                }
            }
        }
        var registry = host.Services.GetRequiredService<ResourceRegistry>(); Assert.Equal(registry.DefinitionHashes[definition.Id], body.GetProperty("definitionHash").GetString());
        string raw = body.GetRawText(); foreach (string implementation in new[] { "Alxarafe.", "System.", "MatInput", "PrimeNG", "Bootstrap", "Tailwind", "DbContext", "cssClass", "componentType" }) Assert.DoesNotContain(implementation, raw, StringComparison.Ordinal);
        using var legacy = await Metadata(client, resourceId: ResourceApiFixtures.Customer); Assert.False((await Body(legacy)).TryGetProperty("presentation", out _));
        using var discoveryRequest = new HttpRequestMessage(HttpMethod.Get, ResourceEndpoints.MetadataPath); discoveryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.Writer);
        using var discovery = await client.SendAsync(discoveryRequest);
        Assert.All((await Body(discovery)).EnumerateArray(), entry => { Assert.False(entry.TryGetProperty("presentation", out _)); Assert.False(entry.TryGetProperty("fields", out _)); });
    }
    [Fact]
    public async Task LayoutChangesInvalidateExistingETagButNeverChangeCrudSchemas()
    {
        var firstDefinition = ComplexFormFixture.Create(WmsModule.Id); var oldForm = firstDefinition.Presentation!.Form;
        var changedForm = new ResourceFormDefinition(oldForm.Tabs.Reverse(), new(UnsavedChangesPolicy.AllowDiscard), oldForm.Actions);
        using var firstHost = Host(firstDefinition); using var first = firstHost.CreateClient();
        using var secondHost = Host(ComplexFormFixture.Create(WmsModule.Id, changedForm)); using var second = secondHost.CreateClient();
        using var initial = await Metadata(first); string etag = initial.Headers.ETag!.ToString();
        using var unchanged = await Metadata(first, etag); Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        using var changed = await Metadata(second, etag); Assert.Equal(HttpStatusCode.OK, changed.StatusCode); Assert.NotEqual(etag, changed.Headers.ETag!.ToString());
        var body = await Body(changed); Assert.NotEqual((await Body(initial)).GetProperty("definitionHash").GetString(), body.GetProperty("definitionHash").GetString());
        Assert.Equal("ALLOW_DISCARD", body.GetProperty("presentation").GetProperty("form").GetProperty("behavior").GetProperty("unsavedChanges").GetString());
        var openApi = await first.GetFromJsonAsync<JsonElement>("/openapi/v1.json"); var changedOpenApi = await second.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        string route = ResourceEndpoints.ResourcePath + "/" + ComplexFormFixture.Id;
        Assert.Equal(openApi.GetProperty("paths").GetProperty(route).GetRawText(), changedOpenApi.GetProperty("paths").GetProperty(route).GetRawText());
        Assert.DoesNotContain("presentation", openApi.GetProperty("paths").GetProperty(route).GetRawText(), StringComparison.Ordinal);
        // Metadata DTO schemas, unlike CRUD schemas, deliberately describe the layout shape.
        var components = openApi.GetProperty("components").GetProperty("schemas");
        Assert.Contains(components.EnumerateObject(), schema => schema.Name.Contains(nameof(ResourcePresentationDto), StringComparison.Ordinal));
    }
    [Fact]
    public async Task InvalidPresentationPreventsHostInitialization()
    {
        using var host = Host(ComplexFormFixture.Create(WmsModule.Id, new([])));
        var failure = await Assert.ThrowsAsync<ResourceValidationException>(async () => { using var client = host.CreateClient(); await client.GetAsync("/health"); });
        Assert.Equal(ResourceValidationCode.InvalidPresentation, failure.Code);
    }
}
