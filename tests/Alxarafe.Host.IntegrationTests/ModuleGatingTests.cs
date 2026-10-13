using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.AspNetCore;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ModuleGatingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string LoginPath = "/api/auth/login";
    private const string AdminPassword = "module-gate-admin-fixture-123";

    [Fact]
    public async Task CanonicalPersistedGateFollowsSecurityAndPreservesCoreRoutesAndStateWithoutRestart()
    {
        using var application = RuntimeProbe.Configure(factory);
        using var anonymous = application.CreateClient();
        var states = application.Services.GetRequiredService<ModuleStateService>();
        var runtime = application.Services.GetRequiredService<ModuleRuntime>();
        var endpoints = application.Services.GetRequiredService<EndpointDataSource>();
        await using var scope = application.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserAdministration>();
        var identity = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AlxarafeUser>>();
        var admin = (await users.CreateAsync("gate-admin-" + Guid.NewGuid() + "@example.test", AdminPassword, true))!;
        var permitted = (await users.CreateAsync("gate-user-" + Guid.NewGuid() + "@example.test", AdminPassword, false))!;
        var account = (await identity.FindByIdAsync(permitted.Id.ToString()))!;
        Assert.True((await identity.AddClaimAsync(account,
            new System.Security.Claims.Claim(Alxarafe.Security.PermissionClaim.Type, RuntimeProbe.Permission))).Succeeded);
        using var authorized = await LoginAsync(application, permitted.Email, AdminPassword);
        using var coreAdmin = await LoginAsync(application, admin.Email, AdminPassword);
        var originals = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var module in runtime.Modules)
            originals[module.Descriptor.Id] = await states.IsEnabledAsync(module.Descriptor.Id);
        var platformFactory = application.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>();
        await using var platform = await platformFactory.CreateDbContextAsync();
        var history = await platform.Database.GetAppliedMigrationsAsync();
        try
        {
            await states.EnableAsync(ConformanceModuleSource.MathId);
            await states.EnableAsync(ConformanceModuleSource.StatisticsId);
            Assert.Equal(HttpStatusCode.OK, (await authorized.GetAsync(RuntimeProbe.Path)).StatusCode);
            var openapi = await ReadOpenApiAsync(anonymous);
            await states.DisableAsync(ConformanceModuleSource.StatisticsId);
            await states.DisableAsync(ConformanceModuleSource.MathId);
            await AssertDisabledAsync(await authorized.GetAsync(RuntimeProbe.Path));
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(RuntimeProbe.Path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await coreAdmin.GetAsync(RuntimeProbe.Path)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health")).StatusCode);
            foreach (var path in new[] { "/api/auth/me", "/api/users", ModuleEndpoints.Path })
                Assert.Equal(HttpStatusCode.OK, (await coreAdmin.GetAsync(path)).StatusCode);
            var disabledOpenapi = await ReadOpenApiAsync(anonymous);
            Assert.Equal(openapi.GetProperty("paths").GetProperty(RuntimeProbe.Path).GetRawText(),
                disabledOpenapi.GetProperty("paths").GetProperty(RuntimeProbe.Path).GetRawText());
            Assert.True(disabledOpenapi.GetProperty("paths").GetProperty(RuntimeProbe.Path).GetProperty("get")
                .GetProperty("responses").TryGetProperty("503", out _));
            Assert.False(disabledOpenapi.GetProperty("paths").GetProperty(LoginPath).GetProperty("post")
                .GetProperty("responses").TryGetProperty("503", out _));
            await users.UpdateAsync(permitted.Id, false, null);
            Assert.Equal(HttpStatusCode.Unauthorized, (await authorized.GetAsync(RuntimeProbe.Path)).StatusCode);
            await users.UpdateAsync(permitted.Id, true, null);
            await states.EnableAsync(ConformanceModuleSource.MathId);
            await states.EnableAsync(ConformanceModuleSource.StatisticsId);
            Assert.Equal(HttpStatusCode.OK, (await authorized.GetAsync(RuntimeProbe.Path)).StatusCode);
            Assert.Equal(history, await platform.Database.GetAppliedMigrationsAsync());
            Assert.Same(runtime, application.Services.GetRequiredService<ModuleRuntime>());
            Assert.Same(endpoints, application.Services.GetRequiredService<EndpointDataSource>());
        }
        finally
        {
            await states.EnableAsync(ConformanceModuleSource.MathId);
            await states.EnableAsync(ConformanceModuleSource.StatisticsId);
            foreach (var module in runtime.Modules.Reverse())
                if (!originals[module.Descriptor.Id]) await states.DisableAsync(module.Descriptor.Id);
            await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users
                .Where(user => user.Id == admin.Id || user.Id == permitted.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task AutomaticModuleMappingProtectsPublicNestedGroupsOnceAndOnlyConvertsDisabledState()
    {
        var module = new NestedHttpModule();
        var store = new CountingStore();
        var states = new ModuleStateService(new ModuleRegistry([new Source(module)]), store);
        await states.SynchronizeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(states);
        builder.Services.AddSingleton(new ModuleRuntime([module]));
        await using var app = builder.Build();
        app.UseExceptionHandler(handler => handler.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        }));
        app.MapAlxarafeModuleEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();
        int reads = store.Reads;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(NestedHttpModule.Path)).StatusCode);
        Assert.Equal(reads + 1, store.Reads);
        await states.DisableAsync(module.Descriptor.Id);
        int writes = store.Changes;
        await AssertDisabledAsync(await client.GetAsync(NestedHttpModule.Path));
        Assert.Equal(reads + 2, store.Reads);
        Assert.Equal(writes, store.Changes);
        Assert.Equal(1, module.Executions);
        await states.EnableAsync(module.Descriptor.Id);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(NestedHttpModule.Path)).StatusCode);
        Assert.Equal(2, module.Executions);
        store.Persisted.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => states.RequireEnabledAsync(module.Descriptor.Id));
        var missing = await client.GetAsync(NestedHttpModule.Path);
        Assert.Equal(HttpStatusCode.InternalServerError, missing.StatusCode);
        Assert.DoesNotContain(ModuleDisabledResponse.ErrorCode, await missing.Content.ReadAsStringAsync());
        // Invalid endpoint ownership must also remain a configuration error.
        var unavailable = new ModuleStateService(new ModuleRegistry([]), store);
        await Assert.ThrowsAsync<ModuleUnavailableException>(() => unavailable.RequireEnabledAsync(module.Descriptor.Id));
        builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(unavailable);
        builder.Services.AddSingleton(new ModuleRuntime([module]));
        await using var invalidApp = builder.Build();
        invalidApp.UseExceptionHandler(handler => handler.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        }));
        invalidApp.MapAlxarafeModuleEndpoints();
        await invalidApp.StartAsync();
        using var invalidClient = invalidApp.GetTestClient();
        var unknown = await invalidClient.GetAsync(NestedHttpModule.Path);
        Assert.Equal(HttpStatusCode.InternalServerError, unknown.StatusCode);
        Assert.DoesNotContain(ModuleDisabledResponse.ErrorCode, await unknown.Content.ReadAsStringAsync());
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> application, string email, string password)
    {
        var client = application.CreateClient();
        var response = await client.PostAsJsonAsync(LoginPath, new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<JsonElement> ReadOpenApiAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");

    private static async Task AssertDisabledAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var property = Assert.Single(body.EnumerateObject());
        Assert.Equal("code", property.Name);
        Assert.Equal(ModuleDisabledResponse.ErrorCode, property.Value.GetString());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Null(response.Headers.RetryAfter);
    }

    private sealed class NestedHttpModule : IAlxarafeModule, IHttpModule
    {
        public const string Path = "/structural/nested/value";
        public ModuleDescriptor Descriptor { get; } = new("third-party-fixture", ModuleApi.CurrentVersion, []);
        public int Executions { get; private set; }
        public void ConfigureServices(IServiceCollection services) { }
        public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapGroup("/structural").MapGroup("/nested")
            .MapGet("/value", () => { Executions++; return "executed"; });
    }

    private sealed record Source(IAlxarafeModule Module) : IModuleSource
    {
        public IEnumerable<IAlxarafeModule> GetModules() => [Module];
    }

    private sealed class CountingStore : IModuleStateStore
    {
        public Dictionary<string, bool> Persisted { get; } = new(StringComparer.Ordinal);
        public int Reads { get; private set; }
        public int Changes { get; private set; }
        public Task<bool?> FindAsync(string moduleId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(Persisted.TryGetValue(moduleId, out var enabled) ? (bool?)enabled : null);
        }
        public Task UpdateAsync(Action<IDictionary<string, bool>> change, CancellationToken cancellationToken = default)
        {
            change(Persisted);
            Changes++;
            return Task.CompletedTask;
        }
    }
}
