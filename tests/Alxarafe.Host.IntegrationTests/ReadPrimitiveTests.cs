using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Alxarafe.Modularity;
using Alxarafe.Modules.Ai;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Http;
using Alxarafe.Modules.Ai.Infrastructure;
using Alxarafe.Modules.Wms;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Alxarafe.Modules.Wms.Http;
using Alxarafe.Modules.Wms.Infrastructure;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ReadPrimitiveTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime, IDisposable
{
    private static readonly string[] PrimitiveUsageFields = ["invocationId", "primitiveId"];
    public void Dispose() { capture.Dispose(); GC.SuppressFinalize(this); }
    private const string UnavailableSignal = "ERBAS_INFORMATION_UNAVAILABLE";
    private const string Password = "Read_primitive_fixture_123";
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private readonly Audit capture = new();
    private WebApplicationFactory<Program> application = null!;
    private HttpClient client = null!;
    private AsyncServiceScope invocationScope;
    private IReadPrimitiveService router = null!;
    private ClaimsPrincipal admin = null!, user = null!;
    private string adminToken = "", userToken = "";
    private string Code => "RP_" + suffix;
    private string AdminEmail => "primitive-admin-" + suffix + "@example.test";
    private string UserEmail => "primitive-user-" + suffix + "@example.test";
    private Guid userId, warehouseId;
    private IDbContextFactory<WmsDbContext> contexts = null!;
    private Func<AiModelRequest, Task<AiModelResponse>>? complete;

    public async Task InitializeAsync()
    {
        using var bootstrap = factory.CreateClient();
        application = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .UseSetting("SecuritySeed:AdminEmail", AdminEmail).UseSetting("SecuritySeed:AdminPassword", Password)
            .UseSetting(WmsModule.DevelopmentUserEmailConfiguration, UserEmail).UseSetting(WmsModule.DevelopmentUserPasswordConfiguration, Password)
            .ConfigureServices(services =>
            {
                services.RemoveAll<IAiModelClient>();
                services.AddSingleton<IAiModelClient>(new Script(request => complete is null
                    ? new DeterministicAiModelClient().CompleteAsync(request) : complete(request)));
                services.AddLogging(logging => logging.AddProvider(capture));
            }));
        client = application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await application.Services.GetRequiredService<ModuleStateService>().EnableAsync(WmsModule.Id);
        invocationScope = application.Services.CreateAsyncScope();
        router = invocationScope.ServiceProvider.GetRequiredService<IReadPrimitiveService>();
        contexts = application.Services.GetRequiredService<IDbContextFactory<WmsDbContext>>();
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
            userId = (await database.Users.SingleAsync(value => value.Email == UserEmail)).Id;
            Guid adminId = (await database.Users.SingleAsync(value => value.Email == AdminEmail)).Id;
            // Principals deliberately omit permission snapshots. The router must use persisted state.
            user = Principal(userId);
            admin = Principal(adminId);
            var permissions = await database.UserClaims.Where(claim => claim.UserId == userId && claim.ClaimType == PermissionClaim.Type)
                .Select(claim => claim.ClaimValue!).ToArrayAsync();
            Assert.Equal(new[] { AiContract.AssistantPermission, InventoryPermissions.WarehousesRead, InventoryPermissions.LocationsRead,
                InventoryPermissions.ItemsRead, InventoryPermissions.InventoryRead }.Order(), permissions.Order());
        }
        adminToken = await Login(AdminEmail);
        userToken = await Login(UserEmail);
        var uom = (await application.Services.GetRequiredService<UomCatalogue>().CreateAsync("U" + suffix, "Unit")).Uom!;
        var service = application.Services.GetRequiredService<InventoryService>();
        var warehouse = (await service.CreateWarehouseAsync(Code, "Registered warehouse", default)).Value!;
        warehouseId = warehouse.Id;
        var location = (await service.CreateLocationAsync(warehouse.Id, "A.01.01", true, default)).Value!;
        var item = (await service.CreateItemAsync(Code, "Registered item", uom.Id, null, default)).Value!;
        Assert.Equal(InventoryOutcome.Created, (await service.RecordAsync(warehouse.Id, item.Id, location.Id,
            new DateTimeOffset(2026, 6, 20, 9, 0, 0, TimeSpan.Zero), 1.25m, uom.Id, InventoryRules.Receipt, "READ-FACT", default)).Outcome);
    }
    public async Task DisposeAsync()
    {
        await application.Services.GetRequiredService<ModuleStateService>().EnableAsync(WmsModule.Id);
        await using var db = await contexts.CreateDbContextAsync();
        await db.Set<InventoryMovement>().Where(row => row.WarehouseId == warehouseId).ExecuteDeleteAsync();
        await db.Set<Location>().Where(row => row.WarehouseId == warehouseId).ExecuteDeleteAsync();
        await db.Set<StockItem>().Where(row => row.Sku == Code).ExecuteDeleteAsync();
        await db.Set<Warehouse>().Where(row => row.Id == warehouseId).ExecuteDeleteAsync();
        await db.Uoms.Where(row => row.Code == "U" + suffix).ExecuteDeleteAsync();
        await using var scope = application.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(row => row.Email == AdminEmail || row.Email == UserEmail).ExecuteDeleteAsync();
        client.Dispose();
        await invocationScope.DisposeAsync();
        await application.DisposeAsync();
    }
    private static ClaimsPrincipal Principal(Guid id) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "fixture"));
    private async Task<string> Login(string email)
    {
        using var result = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }
    private ReadPrimitiveCommand Command(string id, ClaimsPrincipal? principal = null, object? input = null) =>
        new(id, JsonSerializer.SerializeToElement(input ?? new { }), principal ?? user, "parent-ai", "correlation", 1);
    private async Task<HttpResponseMessage> Ask(string text, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AiAssistantEndpoints.Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? userToken);
        request.Headers.AcceptLanguage.ParseAdd("es");
        request.Content = JsonContent.Create(new { message = text });
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task PersistedPermissionCataloguesAndRealReadOutputsAreDifferential()
    {
        Assert.Equal(5, (await router.EffectiveAsync(admin)).Count);
        var effective = await router.EffectiveAsync(user);
        Assert.Equal(4, effective.Count);
        Assert.Contains(effective, primitive => primitive.Id == WmsReadPrimitives.Inventory);
        Assert.DoesNotContain(effective, primitive => primitive.Id == WmsReadPrimitives.Movements);
        var inventory = await router.InvokeAsync(Command(WmsReadPrimitives.Inventory, input: new { warehouseId }));
        Assert.Equal(ReadPrimitiveOutcome.Succeeded, inventory.Outcome);
        var balance = Assert.Single(inventory.Output!.Value.GetProperty("items").EnumerateArray());
        Assert.Equal(1.25m, balance.GetProperty("quantity").GetDecimal());
        Assert.Equal(Code, balance.GetProperty("warehouseCode").GetString());
        Assert.Equal(Code, balance.GetProperty("sku").GetString());
        Assert.Equal("A.01.01", balance.GetProperty("locationCode").GetString());
        Assert.False(balance.TryGetProperty("itemId", out _));
        Assert.Equal("U" + suffix, balance.GetProperty("uom").GetString());
        var history = await router.InvokeAsync(Command(WmsReadPrimitives.Movements, admin, new { warehouseId }));
        Assert.Equal(ReadPrimitiveOutcome.Succeeded, history.Outcome);
        Assert.Single(history.Output!.Value.GetProperty("items").EnumerateArray());
        var forged = Principal(userId);
        ((ClaimsIdentity)forged.Identity!).AddClaims([new Claim(CoreUserClaims.Admin, "true"), new Claim(PermissionClaim.Type, InventoryPermissions.MovementsRead)]);
        Assert.Equal(ReadPrimitiveOutcome.Denied, (await router.InvokeAsync(Command(WmsReadPrimitives.Movements, forged))).Outcome);
        Assert.Equal(ReadPrimitiveOutcome.NotFound, (await router.InvokeAsync(Command("wms.movements.create", admin))).Outcome);
        await application.Services.GetRequiredService<ModuleStateService>().DisableAsync(WmsModule.Id);
        Assert.Empty(await router.EffectiveAsync(admin));
        Assert.Equal(ReadPrimitiveOutcome.Unavailable, (await router.InvokeAsync(Command(WmsReadPrimitives.Inventory, admin))).Outcome);
    }

    [Fact]
    public async Task RevocationAfterInitialDiscoveryRejectsExecutionUsingTheOldPrincipal()
    {
        Assert.Contains(await router.EffectiveAsync(user), primitive => primitive.Id == WmsReadPrimitives.Inventory);
        await RevokeInventory();
        var result = await router.InvokeAsync(Command(WmsReadPrimitives.Inventory));
        Assert.Equal(ReadPrimitiveOutcome.Denied, result.Outcome);
        Assert.Null(result.Output);
        Assert.Null(result.InvocationId);
        Assert.DoesNotContain(await router.EffectiveAsync(user), primitive => primitive.Id == WmsReadPrimitives.Inventory);
        Assert.Contains(capture.Lines, line => line.Contains("primitive=" + WmsReadPrimitives.Inventory, StringComparison.Ordinal)
            && line.Contains("outcome=Denied", StringComparison.Ordinal));
    }
    private async Task RevokeInventory()
    {
        await using var scope = application.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().UserClaims.Where(claim => claim.UserId == userId
            && claim.ClaimType == PermissionClaim.Type && claim.ClaimValue == InventoryPermissions.InventoryRead).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task ModelNeverReceivesUnauthorizedMovementsAndUsesRealPrimitiveProvenance()
    {
        int turns = 0;
        complete = request =>
        {
            Assert.DoesNotContain(request.Tools, tool => tool.PrimitiveId == WmsReadPrimitives.Movements);
            Assert.Contains(request.Tools, tool => tool.PrimitiveId == WmsReadPrimitives.Inventory);
            if (turns++ == 0) return Task.FromResult(new AiModelResponse(null,
                [new("inventory", "wms_inventory_list", JsonSerializer.SerializeToElement(new { warehouseId }))]));
            var toolOutput = JsonDocument.Parse(request.Messages.Last(message => message.Role == AiRoles.Tool).Content!).RootElement;
            Assert.Equal(1.25m, Assert.Single(toolOutput.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetDecimal());
            return Task.FromResult(new AiModelResponse("ERBAS tiene registradas 1,25 unidades.", []));
        };
        using var response = await Ask("Consulta el inventario registrado.");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var usage = Assert.Single(result.GetProperty("toolsUsed").EnumerateArray());
        Assert.Equal(PrimitiveUsageFields, usage.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(WmsReadPrimitives.Inventory, usage.GetProperty("primitiveId").GetString());
        string aiId = result.GetProperty("invocationId").GetString()!;
        Assert.NotEqual(aiId, usage.GetProperty("invocationId").GetString());
        Assert.Contains(capture.Lines, line => line.Contains("aiInvocationId=" + aiId, StringComparison.Ordinal)
            && line.Contains("sequence=1", StringComparison.Ordinal) && line.Contains("outcome=Succeeded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RevocationBetweenModelDiscoveryAndToolCallIsAFunctionalAccessLimitation()
    {
        complete = async request =>
        {
            Assert.Contains(request.Tools, tool => tool.PrimitiveId == WmsReadPrimitives.Inventory);
            await RevokeInventory();
            return new(null, [new("revoked", "wms_inventory_list", JsonSerializer.SerializeToElement(new { warehouseId }))]);
        };
        using var response = await Ask("Consulta datos registrados.");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("No puedo determinarlo con la información a la que tengo acceso.", result.GetProperty("answer").GetString());
        Assert.Empty(result.GetProperty("toolsUsed").EnumerateArray());
        Assert.Contains(capture.Lines, line => line.Contains("outcome=Denied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdminCanComposeExistingCapabilityAndPrimitiveWithDistinctProvenance()
    {
        int turns = 0;
        complete = request =>
        {
            Assert.Contains(request.Tools, tool => tool.PrimitiveId == WmsReadPrimitives.Movements);
            return Task.FromResult(turns++ == 0 ? new AiModelResponse(null,
                [new("unit", "wms_uoms_list", JsonSerializer.SerializeToElement(new { })),
                 new("stock", "wms_inventory_list", JsonSerializer.SerializeToElement(new { warehouseId }))])
                : new AiModelResponse("Se han consultado datos registrados.", []));
        };
        using var response = await Ask("Composición de lecturas.", adminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var tools = result.GetProperty("toolsUsed").EnumerateArray().ToArray();
        Assert.Equal(2, tools.Length);
        Assert.Equal("wms.uoms.list", tools[0].GetProperty("capabilityId").GetString());
        Assert.Equal(WmsReadPrimitives.Inventory, tools[1].GetProperty("primitiveId").GetString());
        Assert.Equal(2, tools.Select(tool => tool.GetProperty("invocationId").GetString()).Distinct().Count());
    }

    [Fact]
    public async Task DeterministicModelRoutesExplicitPrimitiveAndFiniteBudgetStopsLoops()
    {
        using (var response = await Ask(WmsReadPrimitives.Inventory))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(WmsReadPrimitives.Inventory, Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("toolsUsed").EnumerateArray()).GetProperty("primitiveId").GetString());
        }
        complete = _ => Task.FromResult(new AiModelResponse(null, [new(Guid.NewGuid().ToString("N"), "wms_inventory_list", JsonSerializer.SerializeToElement(new { warehouseId }))]));
        int before = capture.Lines.Count(line => line.Contains("READ primitive invocation", StringComparison.Ordinal));
        using var failure = await Ask("Boucle de prueba.");
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.Equal(8, capture.Lines.Count(line => line.Contains("READ primitive invocation", StringComparison.Ordinal)) - before);
    }

    [Fact]
    public async Task InvalidModelArgumentsCanBeCorrectedWithoutLosingTheOriginalBudgetOrProvenance()
    {
        int turns = 0;
        complete = request =>
        {
            if (turns++ == 0)
                return Task.FromResult(new AiModelResponse(null, [new("invalid", "wms_items_list", JsonSerializer.SerializeToElement(new { sku = "INVALID SKU" }))]));
            if (turns == 2)
            {
                var error = JsonSerializer.Deserialize<JsonElement>(request.Messages[^1].Content!);
                Assert.Equal("invalid_arguments", error.GetProperty("status").GetString());
                return Task.FromResult(new AiModelResponse(null, [new("corrected", "wms_items_list", JsonSerializer.SerializeToElement(new { sku = Code }))]));
            }
            var output = JsonSerializer.Deserialize<JsonElement>(request.Messages[^1].Content!);
            Assert.Equal(Code, Assert.Single(output.GetProperty("items").EnumerateArray()).GetProperty("sku").GetString());
            return Task.FromResult(new AiModelResponse("ERBAS tiene registrada la referencia consultada.", []));
        };
        using var response = await Ask("Consulta una referencia por su descripción.", adminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(WmsReadPrimitives.Items, Assert.Single(body.GetProperty("toolsUsed").EnumerateArray()).GetProperty("primitiveId").GetString());
        Assert.Contains(capture.Lines, line => line.Contains("sequence=1", StringComparison.Ordinal) && line.Contains("outcome=InvalidRequest", StringComparison.Ordinal));
        Assert.Contains(capture.Lines, line => line.Contains("sequence=2", StringComparison.Ordinal) && line.Contains("outcome=Succeeded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepeatedInvalidArgumentsConsumeTheExistingEightCallBudget()
    {
        int calls = 0;
        complete = _ =>
        {
            calls++;
            return Task.FromResult(new AiModelResponse(null, [new("invalid", "wms_items_list", JsonSerializer.SerializeToElement(new { sku = "INVALID SKU" }))]));
        };
        using var response = await Ask("Consulta una referencia.", adminToken);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(8, calls);
        Assert.Equal(8, capture.Lines.Count(line => line.Contains("primitive=" + WmsReadPrimitives.Items, StringComparison.Ordinal)
            && line.Contains("outcome=InvalidRequest", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task UnresolvedModelSignalProducesOnlyFunctionalLocalizedText()
    {
        complete = _ => Task.FromResult(new AiModelResponse(UnavailableSignal, []));
        using var response = await Ask("Información que no se puede determinar.");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("No puedo determinarlo con la información que ERBAS pone a mi disposición actualmente.", result.GetProperty("answer").GetString());
        Assert.Empty(result.GetProperty("toolsUsed").EnumerateArray());
    }

    [Fact]
    public async Task ProviderFailureAfterReadRetainsPrivateProvenanceWithoutPayloads()
    {
        int turns = 0;
        complete = _ => turns++ == 0 ? Task.FromResult(new AiModelResponse(null,
            [new("read", "wms_inventory_list", JsonSerializer.SerializeToElement(new { warehouseId }))]))
            : throw new AiModelUnavailableException("private-provider-error");
        using var response = await Ask("Consulta registrada.");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("private-provider-error", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(capture.Lines, line => line.Contains("AI invocation ", StringComparison.Ordinal)
            && line.Contains("tools=primitive:" + WmsReadPrimitives.Inventory, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"url\":\"http://example.test\"}")]
    [InlineData("{\"limit\":101}")]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"warehouseId\":\"invalid\"}")]
    [InlineData("{\"offset\":1,\"offset\":2}")]
    public async Task PrimitiveAdapterRejectsInvalidOrInfrastructureInput(string json)
    {
        var command = Command(WmsReadPrimitives.Inventory) with { Input = JsonDocument.Parse(json).RootElement.Clone() };
        Assert.Equal(ReadPrimitiveOutcome.InvalidRequest, (await router.InvokeAsync(command)).Outcome);
    }
    private sealed class Script(Func<AiModelRequest, Task<AiModelResponse>> action) : IAiModelClient
    {
        public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken = default) => action(request);
    }
    private sealed class Audit : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Sink(Lines);
        public void Dispose() { }
        private sealed class Sink(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => lines.Enqueue(formatter(state, exception));
        }
    }
}
