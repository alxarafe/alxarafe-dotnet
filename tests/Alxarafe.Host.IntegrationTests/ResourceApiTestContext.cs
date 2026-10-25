using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms;
using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.Resources.Runtime;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ResourceApiTestContext : IAsyncLifetime, IAsyncDisposable
{
    private const string Password = "resource-api-fixture-password";
    private const string SecondReference = "test_b";
    private readonly TenantApplicationFactory factory = new();
    private readonly string tenantB = "api-" + Guid.NewGuid().ToString("N");
    private readonly List<Guid> users = [];
    private HttpClient bootstrap = null!;
    internal WebApplicationFactory<Program> Host { get; private set; } = null!;
    internal HttpClient Client { get; private set; } = null!;
    internal ResourceApiFixtures.Store Store { get; } = new();
    internal string Writer { get; private set; } = "";
    internal string Reader { get; private set; } = "";
    internal string Denied { get; private set; } = "";
    internal string NoTenant { get; private set; } = "";
    internal string MultipleTenants { get; private set; } = "";
    internal Guid WriterId { get; private set; }
    private bool enabled;
    public async Task InitializeAsync()
    {
        bootstrap = factory.CreateClient();
        var states = factory.Services.GetRequiredService<ModuleStateService>(); enabled = await states.IsEnabledAsync(WmsModule.Id); await states.EnableAsync(WmsModule.Id);
        await using var platform = await Platform();
        platform.Tenants.Add(new TenantRow { Id = tenantB, Key = tenantB, DisplayName = "API test tenant", Active = true, DatabaseReference = SecondReference });
        await platform.SaveChangesAsync();
        Writer = await User([DevelopmentTenantBootstrap.TenantId], true, true); WriterId = users[^1];
        Reader = await User([DevelopmentTenantBootstrap.TenantId], true, false);
        Denied = await User([DevelopmentTenantBootstrap.TenantId], false, false);
        NoTenant = await User([], true, true);
        MultipleTenants = await User([DevelopmentTenantBootstrap.TenantId, tenantB], true, true);
        Host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IResourceDefinitionProvider>(new ResourceApiFixtures.Provider());
            services.AddSingleton(Store);
            foreach (string resource in new[] { ResourceApiFixtures.Customer, ResourceApiFixtures.Country })
            {
                services.AddSingleton(new ResourceRepositoryBinding(resource, ResourceRepositoryScope.Tenant));
                services.AddScoped<IResourceRepository>(provider => new ResourceApiFixtures.Repository(resource, provider.GetRequiredService<IRequestTenantContext>(), Store));
            }
            services.AddScoped<IResourceOperationValidator, ResourceApiFixtures.DeleteValidator>();
        }));
        Client = Host.CreateClient(new() { AllowAutoRedirect = false });
    }
    async ValueTask IAsyncDisposable.DisposeAsync() => await DisposeAsync();
    public async Task DisposeAsync()
    {
        Client.Dispose(); Host.Dispose(); bootstrap.Dispose();
        await using var platform = await Platform();
        await platform.TenantMemberships.Where(row => users.Contains(row.UserId)).ExecuteDeleteAsync();
        await platform.Tenants.Where(row => row.Id == tenantB).ExecuteDeleteAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(row => users.Contains(row.Id)).ExecuteDeleteAsync();
        if (!enabled) await factory.Services.GetRequiredService<ModuleStateService>().DisableAsync(WmsModule.Id);
        factory.Dispose();
    }
    internal void Reset()
    {
        Store.Records.Clear(); Store.Calls = 0; Store.Mutations = 0;
        Seed(ResourceApiFixtures.Customer, ResourceApiFixtures.FirstId, "C-1", "Water");
        Seed(ResourceApiFixtures.Country, ResourceApiFixtures.FirstId, "CT-1", "Country");
    }
    internal void Seed(string resource, Guid id, string code, string name) => Store.Records[(DevelopmentTenantBootstrap.TenantId, resource, id)] =
        ResourceApiFixtures.Record(("id", new UuidValue(id)), ("code", new TextValue(code)), ("name", new TextValue(name)), ("active", new BooleanValue(true)));
    internal static string Path(string resource = ResourceApiFixtures.Customer, Guid? id = null) => ResourceEndpoints.ResourcePath + "/" + resource + (id is null ? "" : "/" + id.Value.ToString("D"));
    internal Task<HttpResponseMessage> Send(HttpMethod method, string path, string? json = null, string? token = null, string? etag = null, bool spoofTenant = false)
    {
        var request = new HttpRequestMessage(method, path);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        if (spoofTenant) { request.Headers.Add("X-Tenant-Id", DevelopmentTenantBootstrap.TenantId); request.Headers.Host = "untrusted.example.test"; }
        return SendAndDispose(request);
    }
    private async Task<HttpResponseMessage> SendAndDispose(HttpRequestMessage request) { using (request) return await Client.SendAsync(request); }
    internal async Task SetWriterPermission(bool allow)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
        var user = (await manager.FindByIdAsync(WriterId.ToString()))!;
        var claim = new Claim(PermissionClaim.Type, ResourceApiFixtures.Write);
        Assert.True((allow ? await manager.AddClaimAsync(user, claim) : await manager.RemoveClaimAsync(user, claim)).Succeeded);
    }
    private async Task<string> User(string[] tenants, bool read, bool write)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserAdministration>().CreateAsync("api-" + Guid.NewGuid().ToString("N") + "@example.test", Password, false))!;
        users.Add(user.Id);
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>(); var account = (await manager.FindByIdAsync(user.Id.ToString()))!;
        if (read) Assert.True((await manager.AddClaimAsync(account, new Claim(PermissionClaim.Type, ResourceApiFixtures.Read))).Succeeded);
        if (write) Assert.True((await manager.AddClaimAsync(account, new Claim(PermissionClaim.Type, ResourceApiFixtures.Write))).Succeeded);
        await using var platform = await Platform(); await platform.TenantMemberships.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();
        platform.TenantMemberships.AddRange(tenants.Select(tenant => new TenantMembershipRow { UserId = user.Id, TenantId = tenant, Active = true })); await platform.SaveChangesAsync();
        using var login = await bootstrap.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); return (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }
    private Task<PlatformDbContext> Platform() => factory.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
}
