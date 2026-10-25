using System.Security.Claims;
using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ResourceRuntimeIntegrationTests(TenantApplicationFactory factory) : IClassFixture<TenantApplicationFactory>, IAsyncLifetime
{
    private const string ResourceId = "fixture.runtime";
    private const string Version = "1.0.0";
    private const string Password = "resource-runtime-fixture-password";
    private const string ReadPermission = "wms.inventory.read";
    private const string WritePermission = "wms.items.write";
    private const string SecondReference = "test_b";
    private const int ConcurrentPairs = 4;
    private static readonly Guid RecordId = Guid.Parse("3578769e-bf18-4930-8772-9be7d7d49d29");
    private readonly string tenantB = "runtime-" + Guid.NewGuid().ToString("N");
    private readonly List<Guid> users = [];
    private Guid a, b, none, noPermission;
    private bool originallyEnabled;
    private HttpClient bootstrap = null!;
    private WebApplicationFactory<Program> host = null!;
    private HttpClient hostClient = null!;

    public async Task InitializeAsync()
    {
        bootstrap = factory.CreateClient();
        var states = factory.Services.GetRequiredService<ModuleStateService>();
        originallyEnabled = await states.IsEnabledAsync(WmsModule.Id);
        await states.EnableAsync(WmsModule.Id);
        await using var platform = await Platform();
        platform.Tenants.Add(new TenantRow { Id = tenantB, Key = tenantB, DisplayName = "Runtime test tenant", DatabaseReference = SecondReference, Active = true });
        await platform.SaveChangesAsync();
        a = await User(DevelopmentTenantBootstrap.TenantId, true);
        b = await User(tenantB, true);
        none = await User(null, true);
        noPermission = await User(DevelopmentTenantBootstrap.TenantId, false);
        host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IResourceDefinitionProvider, Provider>();
            services.AddSingleton(new ResourceRepositoryBinding(ResourceId, ResourceRepositoryScope.Tenant));
            services.AddScoped<IResourceRepository, DatabaseProbe>();
        }));
        hostClient = host.CreateClient();
    }

    public async Task DisposeAsync()
    {
        hostClient.Dispose(); host.Dispose(); bootstrap.Dispose();
        await using var platform = await Platform();
        await platform.TenantMemberships.Where(row => users.Contains(row.UserId)).ExecuteDeleteAsync();
        await platform.Tenants.Where(row => row.Id == tenantB).ExecuteDeleteAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(row => users.Contains(row.Id)).ExecuteDeleteAsync();
        if (!originallyEnabled) await factory.Services.GetRequiredService<ModuleStateService>().DisableAsync(WmsModule.Id);
    }

    [Fact]
    public async Task ConcurrentScopesResolveRealMembershipAndIndependentPostgresDatabases()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, ConcurrentPairs).SelectMany(_ => new[]
        {
            Read(a, tenantB), Read(b, DevelopmentTenantBootstrap.TenantId)
        }));
        for (int i = 0; i < results.Length; i++)
            Assert.Equal(i % 2 == 0 ? TenantDatabaseValidation.Primary : TenantDatabaseValidation.Secondary, results[i]);
        Assert.Equal(ResourceRuntimeError.TenantRequired, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => Read(none, tenantB))).Code);
    }

    [Fact]
    public async Task PersistedPermissionsModuleStateAndMembershipAreAuthoritativeForEveryInvocation()
    {
        Assert.Equal(ResourceRuntimeError.Forbidden, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => Read(noPermission, tenantB))).Code);
        await InScope(a, async (runtime, probe) =>
        {
            Assert.Single((await runtime.ListAsync(ResourceId)).Items);
            int calls = probe.Calls;
            Assert.Equal(ResourceRuntimeError.Forbidden, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => runtime.CreateAsync(ResourceId, Record(("code", new TextValue("new")))))).Code);
            Assert.Equal(ResourceRuntimeError.Forbidden, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => runtime.UpdateAsync(ResourceId, Identifier(), Record(("code", new TextValue("new")))))).Code);
            Assert.Equal(calls, probe.Calls);
        });
        var states = host.Services.GetRequiredService<ModuleStateService>();
        try
        {
            await states.DisableAsync(WmsModule.Id);
            Assert.Equal(ResourceRuntimeError.ModuleDisabled, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => Read(a, tenantB))).Code);
            Assert.Contains(host.Services.GetRequiredService<ModuleRegistry>().Descriptors, module => module.Id == WmsModule.Id);
        }
        finally { await states.EnableAsync(WmsModule.Id); }
        await using var administration = host.Services.CreateAsyncScope();
        var manager = administration.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
        var account = (await manager.FindByIdAsync(a.ToString()))!;
        await InScope(a, async (runtime, probe) =>
        {
            await runtime.GetAsync(ResourceId, Identifier());
            Assert.True((await manager.RemoveClaimAsync(account, new Claim(PermissionClaim.Type, ReadPermission))).Succeeded);
            int calls = probe.Calls;
            Assert.Equal(ResourceRuntimeError.Forbidden, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => runtime.GetAsync(ResourceId, Identifier()))).Code);
            Assert.Equal(calls, probe.Calls);
        });
    }

    [Fact]
    public void DuplicateBindingsFailDuringHostInitialization()
    {
        using var invalid = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IResourceDefinitionProvider, Provider>();
            services.AddSingleton(new ResourceRepositoryBinding(ResourceId, ResourceRepositoryScope.Tenant));
            services.AddSingleton(new ResourceRepositoryBinding(ResourceId, ResourceRepositoryScope.Tenant));
        }));
        Assert.Throws<InvalidOperationException>(() => invalid.CreateClient());
    }

    private async Task<string> Read(Guid user, string spoofedTenant)
    {
        string database = "";
        await InScope(user, async (runtime, _) => database = ((TextValue)(await runtime.GetAsync(ResourceId, Identifier())).Values["code"]).Value, spoofedTenant);
        return database;
    }
    private async Task InScope(Guid user, Func<ResourceRuntime, DatabaseProbe, Task> action, string spoofedTenant = "not-authoritative")
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString()),
                // Forged request permission must never replace the persisted Identity claims.
                new Claim(PermissionClaim.Type, ReadPermission), new Claim(PermissionClaim.Type, WritePermission)], "fixture"))
        };
        accessor.HttpContext.Request.Headers["X-Tenant-Id"] = spoofedTenant;
        accessor.HttpContext.Request.Host = new HostString("untrusted.example.test");
        try
        {
            var probe = Assert.IsType<DatabaseProbe>(Assert.Single(scope.ServiceProvider.GetServices<IResourceRepository>()));
            await action(scope.ServiceProvider.GetRequiredService<ResourceRuntime>(), probe);
        }
        finally { accessor.HttpContext = null; }
    }
    private async Task<Guid> User(string? tenantId, bool permitted)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserAdministration>().CreateAsync("runtime-" + Guid.NewGuid().ToString("N") + "@example.test", Password, false))!;
        users.Add(user.Id);
        if (permitted)
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
            var account = (await manager.FindByIdAsync(user.Id.ToString()))!;
            Assert.True((await manager.AddClaimAsync(account, new Claim(PermissionClaim.Type, ReadPermission))).Succeeded);
        }
        await using var platform = await Platform();
        await platform.TenantMemberships.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();
        if (tenantId is not null) platform.TenantMemberships.Add(new TenantMembershipRow { UserId = user.Id, TenantId = tenantId, Active = true });
        await platform.SaveChangesAsync();
        return user.Id;
    }
    private Task<PlatformDbContext> Platform() => factory.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
    private static ResourceIdentifier Identifier() => new("id", new UuidValue(RecordId));
    private static ResourceRecord Record(params (string Name, ScalarValue Value)[] values) => new(values.Select(value => new KeyValuePair<string, ScalarValue>(value.Name, value.Value)));
    private sealed class Provider : IResourceDefinitionProvider
    {
        public IReadOnlyCollection<ResourceDefinition> GetResourceDefinitions() => [new(ResourceId, Version, WmsModule.Id, new("id"),
            [new("id", ScalarType.Uuid, readOnly: true, immutable: true), new("code", ScalarType.String, required: true, capabilities: new(true, true, true))],
            [ResourceOperation.Get, ResourceOperation.List, ResourceOperation.Create, ResourceOperation.Update],
            new([new(ResourceOperation.Get, [ReadPermission]), new(ResourceOperation.List, [ReadPermission]),
                new(ResourceOperation.Create, [WritePermission]), new(ResourceOperation.Update, [WritePermission])]))];
    }
    private sealed class DatabaseProbe(ITenantDatabaseResolver databases) : IResourceRepository
    {
        public string ResourceId => ResourceRuntimeIntegrationTests.ResourceId;
        public int Calls { get; private set; }
        public async Task<ResourceRecord?> GetAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
        {
            Calls++;
            await using var connection = new NpgsqlConnection(await databases.ResolveAsync(cancellationToken));
            await connection.OpenAsync(cancellationToken);
            await using var query = new NpgsqlCommand("SELECT current_database()", connection);
            string database = (string)(await query.ExecuteScalarAsync(cancellationToken))!;
            return Record(("id", Identifier().Value), ("code", new TextValue(database)));
        }
        public async Task<ResourcePage> ListAsync(ResourceQuery query, CancellationToken cancellationToken = default) =>
            new([(await GetAsync(Identifier(), cancellationToken))!], query.Offset, query.Limit);
        public Task<ResourceRecord> CreateAsync(ResourceRecord values, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Probe must not receive mutations.");
        public Task<ResourceRecord?> UpdateAsync(ResourceIdentifier identifier, ResourceRecord patch, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Probe must not receive mutations.");
        public Task<bool> DeleteAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Probe must not receive mutations.");
    }
}
