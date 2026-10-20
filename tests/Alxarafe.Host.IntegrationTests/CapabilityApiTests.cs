using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class CapabilityApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string Password = "capability-test-password";
    private readonly List<Guid> users = [];
    private HttpClient client = null!;
    private string admin = "";
    private string ordinary = "";
    private ModuleStateService states = null!;
    private static readonly string[] Ids = ["wms.item-families.list", "wms.storage-attributes.list", "wms.uoms.list"];
    private static readonly string[] PagePropertyNames = ["items", "limit", "offset", "order", "total"];
    private static readonly string[] CapabilityPropertyNames = ["description", "effect", "id", "inputSchema", "module", "name", "outputSchema", "requiredPermissions", "version"];

    public async Task InitializeAsync()
    {
        client = factory.CreateClient();
        states = factory.Services.GetRequiredService<ModuleStateService>();
        await using var scope = factory.Services.CreateAsyncScope();
        var administration = scope.ServiceProvider.GetRequiredService<UserAdministration>();
        foreach (bool isAdmin in new[] { true, false })
        {
            var user = await administration.CreateAsync("capability-" + Guid.NewGuid().ToString("N") + "@example.test", Password, isAdmin);
            users.Add(user!.Id);
            var login = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password });
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
            if (isAdmin) admin = token; else ordinary = token;
        }
    }

    public async Task DisposeAsync()
    {
        await states.EnableAsync(WmsModule.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(user => users.Contains(user.Id)).ExecuteDeleteAsync();
        client.Dispose();
    }

    [Fact]
    public async Task CatalogueAndDetailsHaveExactDescriptorsAndStablePaging()
    {
        var page = await Body(await Get(CapabilityEndpoints.Path, admin), HttpStatusCode.OK);
        Assert.Equal(PagePropertyNames, page.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal(0, page.GetProperty("offset").GetInt32());
        Assert.Equal(50, page.GetProperty("limit").GetInt32());
        Assert.Equal("[{\"field\":\"id\",\"direction\":\"asc\"}]", page.GetProperty("order").GetRawText());
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(Ids, items.Select(x => x.GetProperty("id").GetString()));
        string[] outputs = ["WmsItemFamilyCollection", "WmsStorageAttributeCollection", "WmsUomCollection"];
        string[] permissions = ["wms.item-families.read", "wms.storage-attributes.read", "wms.uoms.read"];
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            Assert.Equal(CapabilityPropertyNames, item.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
            Assert.Equal("erbas.wms", item.GetProperty("module").GetString());
            Assert.Equal("1.0.0", item.GetProperty("version").GetString());
            Assert.Equal("read", item.GetProperty("effect").GetString());
            Assert.Equal("{\"$ref\":\"#/components/schemas/CapabilityListInput\"}", item.GetProperty("inputSchema").GetRawText());
            Assert.Equal("#/components/schemas/" + outputs[i], item.GetProperty("outputSchema").GetProperty("$ref").GetString());
            Assert.Equal(permissions[i], Assert.Single(item.GetProperty("requiredPermissions").EnumerateArray()).GetString());
            var detail = await Body(await Get(CapabilityEndpoints.Path + "/" + Ids[i], admin), HttpStatusCode.OK);
            Assert.Equal(item.GetRawText(), detail.GetRawText());
        }
        var slice = await Body(await Get(CapabilityEndpoints.Path + "?offset=1&limit=1", admin), HttpStatusCode.OK);
        Assert.Equal(3, slice.GetProperty("total").GetInt32());
        Assert.Equal(Ids[1], Assert.Single(slice.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        var hidden = await Body(await Get(CapabilityEndpoints.Path + "?offset=1&limit=1", ordinary), HttpStatusCode.OK);
        Assert.Equal(0, hidden.GetProperty("total").GetInt32());
        Assert.Empty(hidden.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task UnauthorizedUnknownAndDisabledDetailsAreMasked()
    {
        await Error(await Get(CapabilityEndpoints.Path + "/" + Ids[0], ordinary), HttpStatusCode.NotFound, "capability_not_found");
        await Error(await Get(CapabilityEndpoints.Path + "/wms.unknown.list", admin), HttpStatusCode.NotFound, "capability_not_found");
        await states.DisableAsync(WmsModule.Id);
        await Error(await Get(CapabilityEndpoints.Path + "/" + Ids[0], admin), HttpStatusCode.NotFound, "capability_not_found");
        var page = await Body(await Get(CapabilityEndpoints.Path, admin), HttpStatusCode.OK);
        Assert.Equal(0, page.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?offset=-1")]
    [InlineData("?offset=1&offset=2")]
    [InlineData("?other=1")]
    public async Task InvalidPaginationIsRejectedAfterAuthentication(string query)
    {
        await Error(await Get(CapabilityEndpoints.Path + query, admin), HttpStatusCode.BadRequest, "invalid_request");
        await Error(await Get(CapabilityEndpoints.Path + query, ""), HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task InvalidDetailIdAndQueryAreRejected()
    {
        await Error(await Get(CapabilityEndpoints.Path + "/Wms.uoms.list", admin), HttpStatusCode.BadRequest, "invalid_request");
        await Error(await Get(CapabilityEndpoints.Path + "/" + Ids[0] + "?x=1", admin), HttpStatusCode.BadRequest, "invalid_request");
    }

    private async Task<HttpResponseMessage> Get(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token != "") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
    private static async Task<JsonElement> Body(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        if (status == HttpStatusCode.Unauthorized) Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        else Assert.Empty(response.Headers.WwwAuthenticate);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code) =>
        ContractErrors.Single(await Body(response, status), code);
}
