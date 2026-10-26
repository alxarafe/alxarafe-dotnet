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
}
