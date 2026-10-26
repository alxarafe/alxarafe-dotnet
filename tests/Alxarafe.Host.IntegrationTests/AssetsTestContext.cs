using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Assets;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class AssetsTestContext : IAsyncLifetime
{
    internal const string SecondReference = "test_b";
    private const string Password = "Assets-fixture-password1";
    internal TenantApplicationFactory Host { get; } = new();
    internal HttpClient Client { get; private set; } = null!;
    private readonly List<Guid> users = [];
    private readonly string tenantB = "assets-" + Guid.NewGuid().ToString("N");
    private readonly ConcurrentDictionary<(string Resource, bool Secondary), ConcurrentBag<Guid>> created = new();
    internal string Full { get; private set; } = "";
    internal string Writer { get; private set; } = "";
    internal string Reader { get; private set; } = "";
    internal string Schema { get; private set; } = "";
    internal string MaintainWithoutRead { get; private set; } = "";
    internal string SensitiveWithoutMaintain { get; private set; } = "";
    internal string NoPermission { get; private set; } = "";
    internal string NoTenant { get; private set; } = "";
    internal string OtherTenant { get; private set; } = "";
    internal Guid FullUser { get; private set; }
    private bool enabled;
    public async Task InitializeAsync()
    {
        Client = Host.CreateClient(new() { AllowAutoRedirect = false });
        var modules = Host.Services.GetRequiredService<ModuleStateService>();
        enabled = await modules.IsEnabledAsync(AssetsModule.Id); await modules.EnableAsync(AssetsModule.Id);
        await using (var platform = await Platform())
        {
            platform.Tenants.Add(new TenantRow { Id = tenantB, Key = tenantB, DisplayName = "Assets test tenant", Active = true, DatabaseReference = SecondReference });
            await platform.SaveChangesAsync();
        }
        await Host.Services.GetRequiredService<AssetsModule>().InitializeAsync(Host.Services);
        Full = await User(AssetsPermissions.All, DevelopmentTenantBootstrap.TenantId); FullUser = users[^1];
        Writer = await User([AssetsPermissions.Read, AssetsPermissions.Maintain], DevelopmentTenantBootstrap.TenantId);
        Reader = await User([AssetsPermissions.Read], DevelopmentTenantBootstrap.TenantId);
        Schema = await User([AssetsPermissions.Read, AssetsPermissions.SchemaManage], DevelopmentTenantBootstrap.TenantId);
        MaintainWithoutRead = await User([AssetsPermissions.Maintain], DevelopmentTenantBootstrap.TenantId);
        SensitiveWithoutMaintain = await User([AssetsPermissions.Read, AssetsPermissions.Sensitive], DevelopmentTenantBootstrap.TenantId);
        NoPermission = await User([], DevelopmentTenantBootstrap.TenantId);
        NoTenant = await User(AssetsPermissions.All, null);
        OtherTenant = await User(AssetsPermissions.All, tenantB);
    }
    public async Task DisposeAsync()
    {
        foreach (bool secondary in new[] { false, true })
        {
            await using var db = await Database(secondary);
            Guid[] Ids(string resource) => created.GetValueOrDefault((resource, secondary))?.ToArray() ?? [];
            var ids = Ids(AssetResources.Asset); await db.Assets.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync();
            ids = Ids(AssetResources.Model); await db.Models.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync();
            ids = Ids(AssetResources.Type); await db.Types.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync();
            ids = Ids(AssetResources.Manufacturer); await db.Manufacturers.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync();
            ids = Ids(AssetResources.Location); await db.Locations.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync();
            ids = Ids(AssetResources.Status); await db.Statuses.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync();
        }
        await using (var platform = await Platform())
        {
            await platform.TenantMemberships.Where(row => users.Contains(row.UserId)).ExecuteDeleteAsync();
            await platform.Tenants.Where(row => row.Id == tenantB).ExecuteDeleteAsync();
        }
        await using var scope = Host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(row => users.Contains(row.Id)).ExecuteDeleteAsync();
        if (!enabled) await Host.Services.GetRequiredService<ModuleStateService>().DisableAsync(AssetsModule.Id);
        Client.Dispose(); await Host.DisposeAsync();
    }
    internal void Track(string resource, Guid id, bool secondary = false) => created.GetOrAdd((resource, secondary), _ => new()).Add(id);
    internal static string Path(string resource = AssetResources.Asset, Guid? id = null) => ResourceEndpoints.ResourcePath + "/" + resource + (id is null ? "" : "/" + id.Value.ToString("D"));
    internal Task<HttpResponseMessage> Send(HttpMethod method, string resource, Guid? id = null, object? body = null, string? token = null) =>
        Raw(method, Path(resource, id), body is null ? null : JsonSerializer.Serialize(body), token ?? Full);
    internal async Task<HttpResponseMessage> Raw(HttpMethod method, string path, string? json, string? token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await Client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Created)
        {
            var record = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsByteArrayAsync());
            Track(path[(ResourceEndpoints.ResourcePath.Length + 1)..], record.GetProperty(AssetFields.Id).GetGuid(), token == OtherTenant);
        }
        return response;
    }
    internal Task<HttpResponseMessage> Action(Guid id, string action, string? token = null, object? parameters = null) =>
        Raw(HttpMethod.Post, ResourceActionEndpoints.Path + "/" + AssetResources.Asset + "/" + action + ResourceActionEndpoints.ExecutionSegment,
            JsonSerializer.Serialize(new { target = new { id }, parameters = parameters ?? new { } }), token ?? Full);
    internal async Task<JsonElement> Create(string resource, object body, string? token = null)
    {
        using var response = await Send(HttpMethod.Post, resource, body: body, token: token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    internal Task<JsonElement> Asset(string? name = null, string? token = null) => Create(AssetResources.Asset, new { name = name ?? "Asset " + Guid.NewGuid(), assetTypeId = AssetsSeed.Laptop }, token);
    internal async Task<AssetsDbContext> Database(bool secondary = false)
    {
        if (!secondary) return await Host.Services.GetRequiredService<IDbContextFactory<AssetsDbContext>>().CreateDbContextAsync();
        var options = new DbContextOptionsBuilder<AssetsDbContext>();
        AssetsStorage.Configure(options, Host.Services.GetRequiredService<IConfiguration>().GetConnectionString(TenantDatabaseResolver.ConnectionPrefix + SecondReference)!);
        return new(options.Options);
    }
    private Task<PlatformDbContext> Platform() => Host.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
    private async Task<string> User(IReadOnlyList<string> permissions, string? tenant)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserAdministration>().CreateAsync("assets-" + Guid.NewGuid().ToString("N") + "@example.test", Password, false))!;
        users.Add(user.Id);
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
        var account = (await manager.FindByIdAsync(user.Id.ToString()))!;
        foreach (string permission in permissions) Assert.True((await manager.AddClaimAsync(account, new Claim(PermissionClaim.Type, permission))).Succeeded);
        await using var platform = await Platform();
        await platform.TenantMemberships.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();
        if (tenant is not null) platform.TenantMemberships.Add(new TenantMembershipRow { UserId = user.Id, TenantId = tenant, Active = true });
        await platform.SaveChangesAsync();
        using var login = await Client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }
}
