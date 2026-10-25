using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modules.Ai;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Infrastructure;
using Alxarafe.Modules.Wms;
using Alxarafe.Modules.Wms.Domain;
using Alxarafe.Modules.Wms.Infrastructure;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class TenantIsolationTests(TenantApplicationFactory factory) : IClassFixture<TenantApplicationFactory>, IAsyncLifetime
{
    private const string Password = "tenant-isolation-fixture-password";
    private const string Sku = "TEST-001";
    private const string FixtureCode = "TENANT-ISOLATION";
    private const string SecondReference = "test_b";
    private const decimal QuantityA = 100;
    private const decimal QuantityB = 900;
    private const int ConcurrentPairs = 8;
    private readonly string tenantB = Guid.NewGuid().ToString();
    private readonly Guid uomId = Guid.NewGuid(), warehouseId = Guid.NewGuid(), locationId = Guid.NewGuid(), itemId = Guid.NewGuid();
    private readonly List<Guid> users = [];
    private HttpClient client = null!;
    private string a = "", b = "", none = "", multiple = "", unprivileged = "";
    private IConfiguration configuration = null!;
    private string Connection(string reference) => configuration.GetConnectionString(TenantDatabaseResolver.ConnectionPrefix + reference)!;
    private string Path => $"/api/wms/inventory?warehouseId={warehouseId}&itemId={itemId}";

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new() { AllowAutoRedirect = false });
        configuration = factory.Services.GetRequiredService<IConfiguration>();
        await using var context = await Platform();
        context.Tenants.Add(new TenantRow { Id = tenantB, Key = tenantB, DisplayName = "Synthetic second tenant", DatabaseReference = SecondReference, Active = true });
        await context.SaveChangesAsync();
        // Same existing module migrations, independently applied to both pre-created databases.
        await new WmsModule().InitializeAsync(factory.Services);
        await Seed(DevelopmentTenantBootstrap.DatabaseReference, QuantityA);
        await Seed(SecondReference, QuantityB);
        a = await User(DevelopmentTenantBootstrap.TenantId, true);
        b = await User(tenantB, true);
        none = await User(null, true);
        unprivileged = await User(DevelopmentTenantBootstrap.TenantId, false);
        multiple = await User(DevelopmentTenantBootstrap.TenantId, true, tenantB);
    }

    public async Task DisposeAsync()
    {
        foreach (string reference in new[] { DevelopmentTenantBootstrap.DatabaseReference, SecondReference })
        {
            await using var context = Business(reference);
            await context.Set<InventoryMovement>().Where(row => row.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await context.Set<Location>().Where(row => row.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await context.Set<StockItem>().Where(row => row.Id == itemId).ExecuteDeleteAsync();
            await context.Set<Warehouse>().Where(row => row.Id == warehouseId).ExecuteDeleteAsync();
            await context.Uoms.Where(row => row.Id == uomId).ExecuteDeleteAsync();
        }
        await using var platform = await Platform();
        await platform.TenantMemberships.Where(row => users.Contains(row.UserId)).ExecuteDeleteAsync();
        await platform.Tenants.Where(row => row.Id == tenantB).ExecuteDeleteAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(row => users.Contains(row.Id)).ExecuteDeleteAsync();
        client.Dispose();
    }

    [Fact]
    public async Task RealHttpRequestsUseIndependentDatabasesEvenWithConflictingIdsSkuAndConcurrentRequests()
    {
        await Assert.ThrowsAsync<TenantAccessException>(() => factory.Services.GetRequiredService<TenantDatabaseResolver>().ResolveAsync());
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        Assert.Same(firstScope.ServiceProvider.GetRequiredService<IRequestTenantContext>(), firstScope.ServiceProvider.GetRequiredService<IRequestTenantContext>());
        Assert.NotSame(firstScope.ServiceProvider.GetRequiredService<IRequestTenantContext>(), secondScope.ServiceProvider.GetRequiredService<IRequestTenantContext>());
        var pairs = Enumerable.Range(0, ConcurrentPairs).SelectMany(_ => new[] { Balance(a, tenantB), Balance(b, DevelopmentTenantBootstrap.TenantId) }).ToArray();
        var values = await Task.WhenAll(pairs);
        for (int i = 0; i < values.Length; i++) Assert.Equal(i % 2 == 0 ? QuantityA : QuantityB, values[i]);
        await using var platform = await Platform();
        Assert.Equal(2, await platform.Tenants.CountAsync(row => row.Id == tenantB || row.Id == DevelopmentTenantBootstrap.TenantId));
        Assert.False(platform.Database.HasPendingModelChanges());
        await platform.Database.OpenConnectionAsync();
        await using var command = platform.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT to_regclass('public.wms_items') IS NULL AND to_regclass('public.ai_usage_records') IS NOT NULL";
        Assert.Equal(true, await command.ExecuteScalarAsync());
        foreach (string reference in new[] { DevelopmentTenantBootstrap.DatabaseReference, SecondReference })
        {
            await using var business = Business(reference);
            Assert.Equal(4, (await business.Database.GetAppliedMigrationsAsync()).Count());
            Assert.False(business.Database.HasPendingModelChanges());
            await business.Database.OpenConnectionAsync();
            await using var query = business.Database.GetDbConnection().CreateCommand();
            query.CommandText = "SELECT to_regclass('public.tenants') IS NULL AND to_regclass('public.ai_usage_records') IS NULL AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name LIKE 'wms_%' AND column_name='tenant_id')";
            Assert.Equal(true, await query.ExecuteScalarAsync());
        }
    }

    [Fact]
    public async Task MembershipAndPermissionsAreBothRequiredAndInactiveOrAmbiguousTenantsFailClosed()
    {
        await Error(none, HttpStatusCode.Forbidden, TenantAccessException.Required);
        await Error(multiple, HttpStatusCode.Forbidden, TenantAccessException.SelectionRequired);
        using (var denied = await Send(unprivileged)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await using var platform = await Platform();
        try
        {
            await platform.Tenants.Where(row => row.Id == tenantB).ExecuteUpdateAsync(update => update.SetProperty(row => row.Active, false));
            await Error(b, HttpStatusCode.Forbidden, TenantAccessException.Inactive);
            Assert.Equal(QuantityA, await Balance(a));
        }
        finally { await platform.Tenants.Where(row => row.Id == tenantB).ExecuteUpdateAsync(update => update.SetProperty(row => row.Active, true)); }
        // Revocation is read from Platform, not from claims or a global cached context.
        await platform.TenantMemberships.Where(row => row.UserId == users[0]).ExecuteDeleteAsync();
        await Error(a, HttpStatusCode.Forbidden, TenantAccessException.Required);
    }

    [Fact]
    public async Task MissingAndAliasedMappingsNeverFallBackToAnotherTenantOrTheControlPlane()
    {
        await using var platform = await Platform();
        string mapping = TenantDatabaseResolver.ConnectionPrefix + SecondReference;
        string original = Connection(SecondReference);
        try
        {
            configuration["ConnectionStrings:" + mapping] = null;
            await Error(b, HttpStatusCode.ServiceUnavailable, TenantAccessException.DatabaseUnavailable);
            Assert.Equal(QuantityA, await Balance(a));
            configuration["ConnectionStrings:" + mapping] = "Password=never-log-tenant-secret;UnknownOption=invalid";
            await Error(b, HttpStatusCode.ServiceUnavailable, TenantAccessException.DatabaseUnavailable);
            var nonTest = new NpgsqlConnectionStringBuilder(original) { Database = "alxarafe_business" };
            configuration["ConnectionStrings:" + mapping] = nonTest.ConnectionString;
            await Error(b, HttpStatusCode.ServiceUnavailable, TenantAccessException.DatabaseUnavailable);
            configuration["ConnectionStrings:" + mapping] = Connection(DevelopmentTenantBootstrap.DatabaseReference);
            await Error(b, HttpStatusCode.ServiceUnavailable, TenantAccessException.DatabaseUnavailable);
            configuration["ConnectionStrings:" + mapping] = configuration.GetConnectionString(PlatformStorage.ConnectionName);
            await Error(b, HttpStatusCode.ServiceUnavailable, TenantAccessException.DatabaseUnavailable);
        }
        finally { configuration["ConnectionStrings:" + mapping] = original; }
        Assert.Equal(QuantityB, await Balance(b));
        platform.TenantMemberships.Add(new TenantMembershipRow { UserId = users[0], TenantId = "unknown-tenant", Active = true });
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => platform.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    [Fact]
    public async Task StartupMigrationDoesNotCreateAMissingBusinessDatabase()
    {
        const string missingDatabase = "alxarafe_missing_tenant_database";
        // Exercise production migration behavior with already-provisioned disposable test databases.
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production));
        var productionConfiguration = production.Services.GetRequiredService<IConfiguration>();
        string key = "ConnectionStrings:" + TenantDatabaseResolver.ConnectionPrefix + SecondReference;
        string original = Connection(SecondReference);
        try
        {
            var missing = new NpgsqlConnectionStringBuilder(original) { Database = missingDatabase };
            productionConfiguration[key] = missing.ConnectionString;
            var failure = await Assert.ThrowsAsync<PostgresException>(() => new WmsModule().InitializeAsync(production.Services));
            Assert.Equal(PostgresErrorCodes.InvalidCatalogName, failure.SqlState);
            await using var context = Business(DevelopmentTenantBootstrap.DatabaseReference);
            await context.Database.OpenConnectionAsync();
            await using var query = context.Database.GetDbConnection().CreateCommand();
            query.CommandText = "SELECT count(*) FROM pg_database WHERE datname = '" + missingDatabase + "'";
            Assert.Equal(0L, await query.ExecuteScalarAsync());
        }
        finally { productionConfiguration[key] = original; }
    }

    [Fact]
    public async Task AuthoritativeTenantDoesNotActivateLinkedAiOverridesOrTenantMetering()
    {
        string legacyKey = "inactive-ai-override-" + Guid.NewGuid().ToString("N");
        await using var platform = await Platform();
        platform.TenantAiSettings.Add(new TenantAiSettingsRow
        {
            TenantId = legacyKey,
            CanonicalTenantId = DevelopmentTenantBootstrap.TenantId,
            ProviderOverride = OllamaAiModelClient.ProviderName,
            ModelOverride = EffectiveAiConfigurationTests.LocalModel
        });
        await platform.SaveChangesAsync();
        using var wire = new EffectiveAiConfigurationTests.Wire();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddConfiguration(EffectiveAiConfigurationTests.Configuration()));
            builder.ConfigureServices(services => services.AddHttpClient(AiModule.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => wire));
        });
        await using var scope = host.Services.CreateAsyncScope();
        var accessor = host.Services.GetRequiredService<IHttpContextAccessor>();
        try
        {
            accessor.HttpContext = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, users[0].ToString())], "fixture"))
            };
            var context = scope.ServiceProvider.GetRequiredService<IRequestTenantContext>();
            Assert.Equal(DevelopmentTenantBootstrap.TenantId, (await context.RequireAsync()).Id);
            Assert.Equal(DevelopmentTenantBootstrap.TenantId, context.TenantId);
            var client = scope.ServiceProvider.GetRequiredService<IAiModelClient>();
            Assert.IsType<GroqAiModelClient>(client);
            var response = await client.CompleteAsync(new AiModelRequest("Fixture", [new(AiRoles.User, "Hello")], []));
            var usage = Assert.Single(response.Executions!);
            Assert.Equal(GroqAiModelClient.ProviderName, usage.Provider);
            Assert.Equal(EffectiveAiConfigurationTests.GlobalModel, usage.Model);
            Assert.Null(usage.BillingMode);
            Assert.Null(usage.ProviderCost);
            Assert.Equal(EffectiveAiConfigurationTests.GlobalModel, Assert.Single(wire.Calls).Model);
        }
        finally
        {
            accessor.HttpContext = null;
            await platform.TenantAiSettings.Where(row => row.TenantId == legacyKey).ExecuteDeleteAsync();
        }
    }

    private async Task<string> User(string? tenantId, bool admin, string? additional = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserAdministration>().CreateAsync("isolation-" + Guid.NewGuid().ToString("N") + "@example.test", Password, admin))!;
        users.Add(user.Id);
        await using var platform = await Platform();
        await platform.TenantMemberships.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();
        if (tenantId is not null) platform.TenantMemberships.Add(new TenantMembershipRow { UserId = user.Id, TenantId = tenantId, Active = true });
        if (additional is not null) platform.TenantMemberships.Add(new TenantMembershipRow { UserId = user.Id, TenantId = additional, Active = true });
        await platform.SaveChangesAsync();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private async Task<HttpResponseMessage> Send(string token, string? spoofedTenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Tenant-Id", spoofedTenant ?? "unknown-tenant");
        request.Headers.Host = "untrusted-tenant.example.test";
        return await client.SendAsync(request);
    }
    private async Task<decimal> Balance(string token, string? spoofedTenant = null)
    {
        using var response = await Send(token, spoofedTenant);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetDecimal();
    }
    private async Task Error(string token, HttpStatusCode status, string code)
    {
        using var response = await Send(token);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
    private Task<PlatformDbContext> Platform() => factory.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
    private WmsDbContext Business(string reference)
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>();
        WmsStorage.Configure(options, Connection(reference), testing: false);
        return new WmsDbContext(options.Options);
    }
    private async Task Seed(string reference, decimal quantity)
    {
        await using var context = Business(reference);
        context.Uoms.Add(new UomRow { Id = uomId, Code = FixtureCode, Description = "Isolation unit" });
        context.Add(Warehouse.Create(warehouseId, FixtureCode, "Isolation warehouse")!);
        context.Add(Location.Create(locationId, warehouseId, "A.01.01", true)!);
        context.Add(StockItem.Create(itemId, Sku, "Isolated item", uomId, null)!);
        context.Add(InventoryMovement.Create(Guid.NewGuid(), warehouseId, itemId, locationId, DateTimeOffset.UnixEpoch,
            quantity, uomId, InventoryRules.Receipt, FixtureCode)!);
        await context.SaveChangesAsync();
    }
}
