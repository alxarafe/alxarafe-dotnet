using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Host;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms;
using Alxarafe.Modules.Wms.Domain;
using Alxarafe.Modules.Wms.Http;
using Alxarafe.Modules.Wms.Infrastructure;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Alxarafe.Host.IntegrationTests;

public sealed class WmsDevelopmentAuthorizationTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPassword = "Authorization_admin_123";
    private const string ReaderPassword = "Authorization_reader_123";
    private const string AdminPasswordConfiguration = "SecuritySeed:AdminPassword";
    private const string AdminEmailConfiguration = "SecuritySeed:AdminEmail";
    private const string LoginPath = "/api/auth/login";
    private static readonly string[] ReaderPermissions = [InventoryPermissions.WarehousesRead, InventoryPermissions.LocationsRead,
        InventoryPermissions.ItemsRead, InventoryPermissions.InventoryRead];

    [Fact]
    public async Task NormalDevelopmentSeedCreatesDifferentialAccessWithoutIdentityNameChecks()
    {
        using var bootstrap = factory.CreateClient();
        string suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string code = "AUTH_" + suffix;
        string unitCode = "U" + suffix;
        string adminEmail = "administrator-" + suffix + "@example.test";
        string readerEmail = "reader-" + suffix + "@example.test";
        using var application = factory.WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Development)
            .UseSetting(AdminEmailConfiguration, adminEmail).UseSetting(AdminPasswordConfiguration, AdminPassword)
            .UseSetting(WmsModule.DevelopmentUserEmailConfiguration, readerEmail)
            .UseSetting(WmsModule.DevelopmentUserPasswordConfiguration, ReaderPassword));
        try
        {
            using var client = application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await application.Services.GetRequiredService<ModuleStateService>().EnableAsync(WmsModule.Id);
            var fixture = Assert.Single(application.Services.GetRequiredService<IOptions<DevelopmentUsersOptions>>().Value.Users);
            Assert.Equal(readerEmail, fixture.Email);
            Assert.Equal(ReaderPermissions.Order(), fixture.Permissions.Order());
            await using (var scope = application.Services.CreateAsyncScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
                var administration = scope.ServiceProvider.GetRequiredService<UserAdministration>();
                var admin = (await manager.FindByEmailAsync(adminEmail))!;
                var reader = (await manager.FindByEmailAsync(readerEmail))!;
                Assert.True((await administration.FindAsync(admin.Id)) is { Enabled: true, Admin: true });
                Assert.True((await administration.FindAsync(reader.Id)) is { Enabled: true, Admin: false });
                Assert.DoesNotContain(await manager.GetClaimsAsync(admin), claim => claim.Type == PermissionClaim.Type);
                var claims = await manager.GetClaimsAsync(reader);
                Assert.Equal(ReaderPermissions.Order(), claims.Where(claim => claim.Type == PermissionClaim.Type).Select(claim => claim.Value).Order());
                string? hash = reader.PasswordHash;
                await SecuritySeed.InitializeAsync(application.Services, application.Services.GetRequiredService<IHostEnvironment>());
                var repeated = (await manager.FindByEmailAsync(readerEmail))!;
                Assert.Equal(hash, repeated.PasswordHash);
                Assert.Equal(ReaderPermissions.Order(), (await manager.GetClaimsAsync(repeated)).Where(claim => claim.Type == PermissionClaim.Type).Select(claim => claim.Value).Order());
            }
            string adminToken = await LoginAsync(client, adminEmail, AdminPassword);
            string userToken = await LoginAsync(client, readerEmail, ReaderPassword);
            var uom = await CreateAsync(UomEndpoints.Path, new { code = unitCode, description = "Authorization unit" });
            var family = await CreateAsync(ItemFamilyEndpoints.Path, new { code, name = "Authorization family" });
            var attribute = await CreateAsync(StorageAttributeEndpoints.Path, new { code, name = "Authorization attribute" });
            var warehouse = await CreateAsync(InventoryEndpoints.Warehouses, new { code, name = "Authorization warehouse" });
            var location = await CreateAsync(InventoryEndpoints.Locations, new { warehouseId = warehouse.GetProperty("id").GetGuid(), code = "A.01.01", enabled = true });
            var item = await CreateAsync(InventoryEndpoints.Items, new { sku = code, description = "Authorization reference", baseUomId = uom.GetProperty("id").GetGuid(), itemFamilyId = family.GetProperty("id").GetGuid() });
            var movementBody = new
            {
                warehouseId = warehouse.GetProperty("id").GetGuid(),
                itemId = item.GetProperty("id").GetGuid(),
                locationId = location.GetProperty("id").GetGuid(),
                occurredAt = new DateTimeOffset(2026, 6, 20, 9, 0, 0, TimeSpan.Zero),
                quantityDelta = 1.25m,
                uomId = uom.GetProperty("id").GetGuid(),
                type = InventoryRules.Receipt,
                externalKey = "AUTHORIZATION-MOVEMENT"
            };
            var movement = await CreateAsync(InventoryEndpoints.Movements, movementBody);
            string associations = ItemFamilyEndpoints.Path + "/" + family.GetProperty("id").GetGuid() + ItemFamilyAssociationEndpoints.AttributePathSuffix;
            await CreateAsync(associations, new { storageAttributeId = attribute.GetProperty("id").GetGuid() });

            foreach (string path in new[] { InventoryEndpoints.Warehouses, InventoryEndpoints.Locations, InventoryEndpoints.Items,
                InventoryEndpoints.Inventory, InventoryEndpoints.Movements })
            {
                using var adminResponse = await SendAsync(client, HttpMethod.Get, path, adminToken);
                Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
                output.WriteLine("AUTH_MATRIX admin GET {0} -> {1}", path, (int)adminResponse.StatusCode);
                using var userResponse = await SendAsync(client, HttpMethod.Get, path, userToken);
                var expected = path == InventoryEndpoints.Movements ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
                Assert.Equal(expected, userResponse.StatusCode);
                output.WriteLine("AUTH_MATRIX user GET {0} -> {1}", path, (int)userResponse.StatusCode);
                if (expected == HttpStatusCode.Forbidden) ContractErrors.Single(await Body(userResponse), "forbidden");
            }
            foreach (var (path, id, allowed) in new[] {
                (InventoryEndpoints.Warehouses, warehouse.GetProperty("id").GetGuid(), true),
                (InventoryEndpoints.Locations, location.GetProperty("id").GetGuid(), true),
                (InventoryEndpoints.Items, item.GetProperty("id").GetGuid(), true),
                (InventoryEndpoints.Movements, movement.GetProperty("id").GetGuid(), false) })
            {
                using var response = await SendAsync(client, HttpMethod.Get, path + "/" + id, userToken);
                Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
            }
            var writes = new (HttpMethod Method, string Path, object? Body)[]
            {
                (HttpMethod.Post, InventoryEndpoints.Warehouses, new { code = code + "_DENIED", name = "Denied" }),
                (HttpMethod.Post, InventoryEndpoints.Locations, new { warehouseId = warehouse.GetProperty("id").GetGuid(), code = "A.01.02", enabled = true }),
                (HttpMethod.Post, InventoryEndpoints.Items, new { sku = code + "_DENIED", description = "Denied", baseUomId = uom.GetProperty("id").GetGuid() }),
                (HttpMethod.Post, InventoryEndpoints.Movements, movementBody with { externalKey = "AUTHORIZATION-DENIED" }),
                (HttpMethod.Post, UomEndpoints.Path, new { code = "D" + suffix, description = "Denied" }),
                (HttpMethod.Post, ItemFamilyEndpoints.Path, new { code = code + "_DENIED", name = "Denied" }),
                (HttpMethod.Post, StorageAttributeEndpoints.Path, new { code = code + "_DENIED", name = "Denied" }),
                (HttpMethod.Post, associations, new { storageAttributeId = attribute.GetProperty("id").GetGuid() }),
                (HttpMethod.Put, associations, new { storageAttributeIds = Array.Empty<Guid>() }),
                (HttpMethod.Patch, associations, new { add = Array.Empty<Guid>(), remove = new[] { attribute.GetProperty("id").GetGuid() } }),
                (HttpMethod.Delete, associations + "/" + attribute.GetProperty("id").GetGuid(), null)
            };
            foreach (var write in writes)
            {
                using var response = await SendAsync(client, write.Method, write.Path, userToken, write.Body);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                ContractErrors.Single(await Body(response), "forbidden");
                output.WriteLine("AUTH_MATRIX user {0} {1} -> {2}", write.Method, write.Path, (int)response.StatusCode);
            }
            var inventory = await Body(await SendAsync(client, HttpMethod.Get, InventoryEndpoints.Inventory + "?warehouseId=" + warehouse.GetProperty("id").GetGuid(), userToken));
            Assert.Equal(1.25m, Assert.Single(inventory.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetDecimal());
            var history = await Body(await SendAsync(client, HttpMethod.Get, InventoryEndpoints.Movements + "?warehouseId=" + warehouse.GetProperty("id").GetGuid(), adminToken));
            Assert.Single(history.GetProperty("items").EnumerateArray());
            var pairs = await Body(await SendAsync(client, HttpMethod.Get, associations, adminToken));
            Assert.Single(pairs.GetProperty("items").EnumerateArray());

            async Task<JsonElement> CreateAsync(string path, object body)
            {
                using var response = await SendAsync(client, HttpMethod.Post, path, adminToken, body);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                output.WriteLine("AUTH_MATRIX admin POST {0} -> {1}", path, (int)response.StatusCode);
                return await Body(response);
            }
        }
        finally
        {
            await using var context = await factory.Services.GetRequiredService<IDbContextFactory<WmsDbContext>>().CreateDbContextAsync();
            var warehouseIds = context.Set<Warehouse>().Where(row => row.Code == code || row.Code == code + "_DENIED").Select(row => row.Id);
            await context.Set<InventoryMovement>().Where(row => warehouseIds.Contains(row.WarehouseId)).ExecuteDeleteAsync();
            await context.Set<Location>().Where(row => warehouseIds.Contains(row.WarehouseId)).ExecuteDeleteAsync();
            await context.Set<StockItem>().Where(row => row.Sku == code || row.Sku == code + "_DENIED").ExecuteDeleteAsync();
            await context.Set<Warehouse>().Where(row => warehouseIds.Contains(row.Id)).ExecuteDeleteAsync();
            var familyIds = context.ItemFamilies.Where(row => row.Code == code || row.Code == code + "_DENIED").Select(row => row.Id);
            await context.ItemFamilyAssociations.Where(row => familyIds.Contains(row.ItemFamilyId)).ExecuteDeleteAsync();
            await context.ItemFamilies.Where(row => familyIds.Contains(row.Id)).ExecuteDeleteAsync();
            await context.StorageAttributes.Where(row => row.Code == code || row.Code == code + "_DENIED").ExecuteDeleteAsync();
            await context.Uoms.Where(row => row.Code == unitCode || row.Code == "D" + suffix).ExecuteDeleteAsync();
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(user => user.Email == readerEmail || user.Email == adminEmail).ExecuteDeleteAsync();
        }
    }

    public static IEnumerable<object[]> ExcludedProfiles =>
        [[Environments.Development, false], [Environments.Production, true], ["Testing", true]];

    [Theory]
    [MemberData(nameof(ExcludedProfiles))]
    public async Task ReaderRequiresDevelopmentAndAnExplicitPassword(string environment, bool configured)
    {
        using var bootstrap = factory.CreateClient();
        string email = "unseeded-" + Guid.NewGuid().ToString("N") + "@example.test";
        using var application = factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment)
            .UseSetting(AdminPasswordConfiguration, "")
            .UseSetting(WmsModule.DevelopmentUserEmailConfiguration, email)
            .UseSetting(WmsModule.DevelopmentUserPasswordConfiguration, configured ? ReaderPassword : ""));
        using var client = application.CreateClient();
        Assert.Empty(application.Services.GetRequiredService<IOptions<DevelopmentUsersOptions>>().Value.Users);
        await using var scope = application.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.AnyAsync(user => user.Email == email));
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsJsonAsync(LoginPath, new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Body(response)).GetProperty("accessToken").GetString()!;
    }
    private static Task<JsonElement> Body(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
