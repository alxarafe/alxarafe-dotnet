using System.Net;
using System.Text.Json;
using Alxarafe.Modules.Assets;
using Alxarafe.Resources.AspNetCore;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class AssetPresentationTests(AssetsTestContext context) : IClassFixture<AssetsTestContext>
{
    private static readonly string[] Tabs = ["general", "purchase", "location", "audit"];
    [Fact]
    public async Task StaticLayoutIsCompleteAndIdenticalAcrossEffectivePermissionProfiles()
    {
        using var writer = await context.Raw(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + AssetResources.Asset, null, context.Full);
        using var reader = await context.Raw(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + AssetResources.Asset, null, context.Reader);
        Assert.Equal(HttpStatusCode.OK, writer.StatusCode); Assert.Equal(HttpStatusCode.OK, reader.StatusCode);
        var admin = JsonDocument.Parse(await writer.Content.ReadAsStringAsync()).RootElement;
        var restricted = JsonDocument.Parse(await reader.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(admin.GetProperty("fields").GetRawText(), restricted.GetProperty("fields").GetRawText());
        Assert.Equal(admin.GetProperty("presentation").GetRawText(), restricted.GetProperty("presentation").GetRawText());
        Assert.True(admin.GetProperty("operations").GetProperty("update").GetProperty("authorized").GetBoolean());
        foreach (string operation in new[] { "create", "update", "delete" }) Assert.False(restricted.GetProperty("operations").GetProperty(operation).GetProperty("authorized").GetBoolean());
        Assert.All(restricted.GetProperty("actions").EnumerateArray(), action => Assert.False(action.GetProperty("authorized").GetBoolean()));
        Assert.NotEqual(writer.Headers.ETag, reader.Headers.ETag);
        var form = admin.GetProperty("presentation").GetProperty("form");
        Assert.Equal(Tabs, form.GetProperty("tabs").EnumerateArray().Select(tab => tab.GetProperty("id").GetString()));
        Assert.Equal(AssetActions.Complete, Assert.Single(form.GetProperty("actions").EnumerateArray()).GetString());
        var tabs = form.GetProperty("tabs").EnumerateArray().ToArray();
        Assert.Equal(AssetActions.Retire, Assert.Single(tabs[0].GetProperty("sections")[2].GetProperty("actions").EnumerateArray()).GetString());
        Assert.Equal(new[] { AssetActions.Assign, AssetActions.Unassign, AssetActions.Transfer }, tabs[2].GetProperty("actions").EnumerateArray().Select(action => action.GetString()));
        Assert.True(tabs[3].GetProperty("sections")[0].GetProperty("initiallyCollapsed").GetBoolean());
        var placed = tabs.SelectMany(tab => tab.GetProperty("sections").EnumerateArray()).SelectMany(section => section.GetProperty("groups").EnumerateArray()).SelectMany(group => group.GetProperty("fields").EnumerateArray()).Select(field => field.GetString()).ToArray();
        Assert.Equal(admin.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()).Order(), placed.Order());
        Assert.Equal(placed.Length, placed.Distinct().Count());
    }
    private async Task<JsonElement> Contextual(string token)
    {
        using var response = await context.Raw(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + AssetResources.Asset + ResourceEndpoints.PresentationSuffix, null, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
    private static string[] Fields(JsonElement form) => form.GetProperty("tabs").EnumerateArray().SelectMany(tab => tab.GetProperty("sections").EnumerateArray())
        .SelectMany(section => section.GetProperty("groups").EnumerateArray()).SelectMany(group => group.GetProperty("fields").EnumerateArray()).Select(field => field.GetString()!).ToArray();
    [Fact]
    public async Task MinimalContextualCreateProducesDraftThenFullEditRetainsExistingBusinessAndImmutableRules()
    {
        var body = await Contextual(context.Full); var presentation = body.GetProperty("presentation");
        Assert.Equal([AssetFields.AssetTypeId, AssetFields.Name], Fields(presentation.GetProperty("create")));
        var created = await context.Create(AssetResources.Asset, new { assetTypeId = AssetsSeed.Laptop, name = "Contextual two-phase Asset" });
        Assert.Equal(AssetLifecycle.Draft, created.GetProperty(AssetFields.Lifecycle).GetString());
        Guid id = created.GetProperty(AssetFields.Id).GetGuid();
        Assert.Contains(AssetFields.AssetTypeId, Fields(presentation.GetProperty("edit")));
        using var edited = await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { name = "Contextual full edit" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode); var persisted = JsonDocument.Parse(await edited.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Contextual full edit", persisted.GetProperty(AssetFields.Name).GetString());
        Assert.Equal(AssetLifecycle.Draft, persisted.GetProperty(AssetFields.Lifecycle).GetString());
        using var immutable = await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { assetTypeId = AssetsSeed.Laptop });
        Assert.Equal(HttpStatusCode.Conflict, immutable.StatusCode);
        await using var db = await context.Database();
        Assert.Equal("Contextual full edit", (await db.Assets.FindAsync(id))!.Name);
    }
    [Fact]
    public async Task AssetListEditAndViewAreGenericStaticDeclarationsSharedByRestrictedAndUnboundProfiles()
    {
        var body = await Contextual(context.Full); var presentation = body.GetProperty("presentation");
        Assert.Equal([AssetFields.Name, AssetFields.AssetTypeId, AssetFields.AssetTag, AssetFields.Lifecycle, AssetFields.Condition, AssetFields.LocationId],
            presentation.GetProperty("list").GetProperty("columns").EnumerateArray().Select(column => column.GetProperty("field").GetString()));
        using var detailed = await context.Raw(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + AssetResources.Asset, null, context.Full);
        var metadata = JsonDocument.Parse(await detailed.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(metadata.GetProperty("definitionHash").GetString(), body.GetProperty("definitionHash").GetString());
        Assert.Equal(metadata.GetProperty("presentation").GetProperty("form").GetRawText(), presentation.GetProperty("edit").GetRawText());
        Assert.Equal(presentation.GetProperty("edit").GetRawText(), presentation.GetProperty("view").GetRawText());
        Assert.Equal(Tabs, presentation.GetProperty("view").GetProperty("tabs").EnumerateArray().Select(tab => tab.GetProperty("id").GetString()));
        Assert.Equal(metadata.GetProperty("fields").GetArrayLength(), Fields(presentation.GetProperty("edit")).Length);
        Assert.True(metadata.GetProperty("fields").EnumerateArray().Single(field => field.GetProperty("name").GetString() == AssetFields.AssetTypeId).GetProperty("immutable").GetBoolean());
        foreach (string token in new[] { context.Reader, context.NoPermission, context.NoTenant })
            Assert.Equal(presentation.GetRawText(), (await Contextual(token)).GetProperty("presentation").GetRawText());
        using var restricted = await context.Raw(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + AssetResources.Asset, null, context.Reader);
        Assert.False(JsonDocument.Parse(await restricted.Content.ReadAsStringAsync()).RootElement.GetProperty("operations").GetProperty("update").GetProperty("authorized").GetBoolean());
    }

}
