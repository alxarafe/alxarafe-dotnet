using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.AspNetCore;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.AiAgent.Infrastructure;
using Alxarafe.Modules.AiAgent.ModuleDefinition;
using Alxarafe.Modules.Catalog.Infrastructure;
using Alxarafe.Modules.Catalog.ModuleDefinition;
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
    private const string AiPath = "/api/ai/knowledge";
    private const string CatalogPath = "/api/catalog/items";
    private const string AdminPassword = "module-gate-admin-fixture-123";

    [Fact]
    public async Task RealModulesUseDynamicPersistedGateAfterSecurityWithoutAffectingCoreOrData()
    {
        using var anonymous = factory.CreateClient();
        using var aiWriter = await LoginAsync("ai-writer@example.test", "Creator_dev_only_123!");
        using var catalogWriter = await LoginAsync("creator@example.test", "Creator_dev_only_123!");
        using var noPermission = await LoginAsync("ai-reader@example.test", "Reader_dev_only_123!");
        var runtime = factory.Services.GetRequiredService<ModuleRuntime>();
        var states = factory.Services.GetRequiredService<ModuleStateService>();
        var available = runtime.Modules.Where(module => module is IHttpModule).ToArray();
        string aiId = Assert.Single(available.OfType<AiAgentModule>()).Descriptor.Id;
        string catalogId = Assert.Single(available.OfType<CatalogModule>()).Descriptor.Id;
        var originals = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var module in available)
        {
            originals.Add(module.Descriptor.Id, await states.IsEnabledAsync(module.Descriptor.Id));
            await states.EnableAsync(module.Descriptor.Id);
        }
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        Guid aiEntryId = Guid.Empty, catalogItemId = Guid.Empty, adminId = Guid.Empty;
        try
        {
            var aiCreate = await aiWriter.PostAsJsonAsync(AiPath, new { question = "Gate survival", answer = "Persistent answer" });
            Assert.Equal(HttpStatusCode.Created, aiCreate.StatusCode);
            var aiEntry = await aiCreate.Content.ReadFromJsonAsync<JsonElement>();
            aiEntryId = aiEntry.GetProperty("id").GetGuid();
            var catalogCreate = await catalogWriter.PostAsJsonAsync(CatalogPath, new { sku = "gate-" + Guid.NewGuid().ToString("N"), name = "Surviving item" });
            Assert.Equal(HttpStatusCode.Created, catalogCreate.StatusCode);
            var catalogItem = await catalogCreate.Content.ReadFromJsonAsync<JsonElement>();
            catalogItemId = catalogItem.GetProperty("id").GetGuid();
            string aiReadPath = AiPath + "/" + aiEntryId;
            string catalogReadPath = CatalogPath + "/" + catalogItemId;
            var openapi = await ReadOpenApiAsync(anonymous);
            var platformFactory = factory.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>();
            await using var platform = await platformFactory.CreateDbContextAsync();
            var history = await platform.Database.GetAppliedMigrationsAsync();
            await using var scope = factory.Services.CreateAsyncScope();
            var admin = await scope.ServiceProvider.GetRequiredService<UserAdministration>()
                .CreateAsync("module-gate-" + Guid.NewGuid().ToString("N") + "@example.test", AdminPassword, true);
            Assert.NotNull(admin);
            adminId = admin.Id;

            // Preserve graph invariants if the built-in dependency declarations evolve.
            foreach (var module in available.Reverse()) await states.DisableAsync(module.Descriptor.Id);
            Assert.False(await states.IsEnabledAsync(aiId));
            Assert.False(await states.IsEnabledAsync(catalogId));
            await AssertDisabledAsync(await aiWriter.GetAsync(aiReadPath));
            await AssertDisabledAsync(await aiWriter.PostAsJsonAsync(AiPath, new { question = "Blocked", answer = "Blocked" }));
            await AssertDisabledAsync(await catalogWriter.GetAsync(catalogReadPath));
            await AssertDisabledAsync(await catalogWriter.PostAsJsonAsync(CatalogPath, new { sku = "blocked", name = "Blocked" }));
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(aiReadPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(catalogReadPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.PostAsJsonAsync(AiPath, new { question = "Denied", answer = "Denied" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.GetAsync(catalogReadPath)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health")).StatusCode);
            using var coreAdmin = await LoginAsync(admin.Email, AdminPassword);
            Assert.Equal(HttpStatusCode.OK, (await coreAdmin.GetAsync("/api/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await coreAdmin.GetAsync("/api/users")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await coreAdmin.GetAsync(ModuleEndpoints.Path)).StatusCode);
            var disabledOpenApi = await ReadOpenApiAsync(anonymous);
            foreach (string path in new[] { AiPath, AiPath + "/{id}", CatalogPath, CatalogPath + "/{id}" })
            {
                Assert.Equal(openapi.GetProperty("paths").GetProperty(path).GetRawText(),
                    disabledOpenApi.GetProperty("paths").GetProperty(path).GetRawText());
                foreach (var operation in disabledOpenApi.GetProperty("paths").GetProperty(path).EnumerateObject())
                    Assert.True(operation.Value.GetProperty("responses").GetProperty("503")
                        .GetProperty("content").TryGetProperty("application/json", out _));
            }
            Assert.False(disabledOpenApi.GetProperty("paths").GetProperty(LoginPath).GetProperty("post")
                .GetProperty("responses").TryGetProperty("503", out _));
            foreach (var module in available) await states.EnableAsync(module.Descriptor.Id);
            var aiRead = await aiWriter.GetAsync(aiReadPath);
            var catalogRead = await catalogWriter.GetAsync(catalogReadPath);
            Assert.Equal(HttpStatusCode.OK, aiRead.StatusCode);
            Assert.Equal(HttpStatusCode.OK, catalogRead.StatusCode);
            Assert.Equal(aiEntry.GetRawText(), (await aiRead.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
            Assert.Equal(catalogItem.GetRawText(), (await catalogRead.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
            Assert.Equal(history, await platform.Database.GetAppliedMigrationsAsync());
            Assert.Same(runtime, factory.Services.GetRequiredService<ModuleRuntime>());
            Assert.Same(endpoints, factory.Services.GetRequiredService<EndpointDataSource>().Endpoints);
        }
        finally
        {
            foreach (var module in available) await states.EnableAsync(module.Descriptor.Id);
            foreach (var module in available.Reverse())
                if (!originals[module.Descriptor.Id]) await states.DisableAsync(module.Descriptor.Id);
            await using var cleanup = factory.Services.CreateAsyncScope();
            await cleanup.ServiceProvider.GetRequiredService<AiAgentDbContext>().Knowledge
                .Where(row => row.Id == aiEntryId).ExecuteDeleteAsync();
            await cleanup.ServiceProvider.GetRequiredService<CatalogDbContext>().Items
                .Where(row => row.Id == catalogItemId).ExecuteDeleteAsync();
            await cleanup.ServiceProvider.GetRequiredService<SecurityDbContext>().Users
                .Where(row => row.Id == adminId).ExecuteDeleteAsync();
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

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = factory.CreateClient();
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
