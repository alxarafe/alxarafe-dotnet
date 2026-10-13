using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ModulesApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string Password = "module-api-native-password";
    private const string BearerScheme = "Bearer";
    private const string LoginPath = "/api/auth/login";
    private static readonly string[] ModuleFields = ["dependencies", "enabled", "id", "moduleApiVersion"];
    private static readonly string[] PageFields = ["items", "limit", "offset", "order", "total"];
    private readonly List<Guid> ownedUsers = [];
    private readonly List<string> ownedModules = [ConformanceModuleSource.MathId, ConformanceModuleSource.StatisticsId];
    private readonly string retired = "retired-module-" + Guid.NewGuid().ToString("N");
    private WebApplicationFactory<Program> application = null!;
    private HttpClient client = null!;
    private string adminToken = "";
    private string userToken = "";
    private ModuleStateService states = null!;

    public async Task InitializeAsync()
    {
        application = factory.WithWebHostBuilder(builder => builder.UseEnvironment(ConformanceModuleSource.EnvironmentName)
            .UseSetting(ConformanceModuleSource.EnabledConfiguration, bool.TrueString));
        client = application.CreateClient();
        states = application.Services.GetRequiredService<ModuleStateService>();
        var store = application.Services.GetRequiredService<IModuleStateStore>();
        // No canonical seeds: verify normal startup synchronization before HTTP mutations.
        Assert.Equal(ModuleStateService.InitialEnabled, await store.FindAsync(ConformanceModuleSource.MathId));
        Assert.Equal(ModuleStateService.InitialEnabled, await store.FindAsync(ConformanceModuleSource.StatisticsId));
        await using var scope = application.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserAdministration>();
        foreach (bool admin in new[] { true, false })
        {
            var user = await users.CreateAsync("native-modules-" + Guid.NewGuid().ToString("N") + "@example.test", Password, admin);
            ownedUsers.Add(user!.Id);
            var token = await LoginAsync(user.Email);
            if (admin) adminToken = token;
            else userToken = token;
        }
        ownedModules.Add(retired);
        await store.UpdateAsync(snapshot => snapshot.Add(retired, false));
    }

    public async Task DisposeAsync()
    {
        await using var scope = application.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users
            .Where(user => ownedUsers.Contains(user.Id)).ExecuteDeleteAsync();
        await using var context = await application.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        await context.ModuleStates.Where(row => ownedModules.Contains(row.ModuleId)).ExecuteDeleteAsync();
        client.Dispose();
        await application.DisposeAsync();
    }

    [Fact]
    public async Task ListingUsesClosedShapeOrdinalOrderingAndFreshStateWhileHidingRetiredRows()
    {
        var page = await Body(await SendAsync(HttpMethod.Get, ModuleEndpoints.Path));
        Assert.Equal(PageFields, page.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(0, page.GetProperty("offset").GetInt32());
        Assert.Equal(CollectionPagination.DefaultLimit, page.GetProperty("limit").GetInt32());
        var registry = application.Services.GetRequiredService<ModuleRegistry>();
        Assert.Equal(registry.Modules.Count, page.GetProperty("total").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(registry.Modules.Select(module => module.Descriptor.Id).Order(StringComparer.Ordinal),
            items.Select(item => item.GetProperty("id").GetString()));
        Assert.Equal("[{\"field\":\"id\",\"direction\":\"asc\"}]", page.GetProperty("order").GetRawText());
        foreach (var item in items)
        {
            var descriptor = registry.Modules.Single(module => module.Descriptor.Id == item.GetProperty("id").GetString()).Descriptor;
            AssertModule(item, descriptor.Id, true, descriptor.Dependencies.Order(StringComparer.Ordinal).ToArray());
        }
        Assert.DoesNotContain(items, item => item.GetProperty("id").GetString() == retired);
        await states.DisableAsync(ConformanceModuleSource.StatisticsId);
        var second = await Body(await SendAsync(HttpMethod.Get, ModuleEndpoints.Path));
        Assert.False(second.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == ConformanceModuleSource.StatisticsId).GetProperty("enabled").GetBoolean());
        Assert.False(await application.Services.GetRequiredService<IModuleStateStore>().FindAsync(retired));
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("1", 1)]
    [InlineData("2", 1)]
    [InlineData("3", CollectionPagination.MaximumLimit)]
    [InlineData("4", 1)]
    [InlineData("999999999999999999999999999999", CollectionPagination.DefaultLimit)]
    public async Task PaginationKeepsTotalAndCapacityAndPreservesBeyondTotalOffsets(string offset, int limit)
    {
        var page = await Body(await SendAsync(HttpMethod.Get, ModuleEndpoints.Path + $"?offset={offset}&limit={limit}"));
        Assert.Equal(offset, page.GetProperty("offset").GetRawText());
        Assert.Equal(limit, page.GetProperty("limit").GetInt32());
        var descriptors = application.Services.GetRequiredService<ModuleRegistry>().Modules.Select(module => module.Descriptor).ToArray();
        Assert.Equal(descriptors.Length, page.GetProperty("total").GetInt32());
        var appliedOffset = System.Numerics.BigInteger.Parse(offset, System.Globalization.CultureInfo.InvariantCulture);
        string[] expected = appliedOffset >= descriptors.Length ? [] : descriptors.Skip((int)appliedOffset).Take(limit).Select(module => module.Id).ToArray();
        Assert.Equal(expected, page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
    }

    [Theory]
    [InlineData("offset=-1")]
    [InlineData("offset=abc")]
    [InlineData("offset=1.5")]
    [InlineData("offset=")]
    [InlineData("offset=0&offset=1")]
    [InlineData("limit=0")]
    [InlineData("limit=-1")]
    [InlineData("limit=abc")]
    [InlineData("limit=1.5")]
    [InlineData("limit=")]
    [InlineData("limit=1&limit=1")]
    public async Task InvalidOrRepeatedQueryIsRejected(string query) =>
        await Error(await SendAsync(HttpMethod.Get, ModuleEndpoints.Path + "?" + query), HttpStatusCode.BadRequest, ModuleEndpoints.InvalidRequest);

    [Fact]
    public async Task AboveMaximumLimitIsRejectedRatherThanClamped() =>
        await Error(await SendAsync(HttpMethod.Get, ModuleEndpoints.Path + "?limit=" + (CollectionPagination.MaximumLimit + 1)),
            HttpStatusCode.BadRequest, ModuleEndpoints.InvalidRequest);

    [Fact]
    public async Task ConflictsAreAtomicAndBothTransitionsArePersistedIdempotentAndRecoverableThroughCore()
    {
        var original = await Snapshot();
        await Error(await Patch(ConformanceModuleSource.MathId, false), HttpStatusCode.Conflict, ModuleEndpoints.DependencyConflict);
        Assert.Equal(original, await Snapshot());
        for (int repetition = 0; repetition < 2; repetition++)
            AssertModule(await Body(await Patch(ConformanceModuleSource.StatisticsId, false)), ConformanceModuleSource.StatisticsId, false, [ConformanceModuleSource.MathId]);
        for (int repetition = 0; repetition < 2; repetition++)
            AssertModule(await Body(await Patch(ConformanceModuleSource.MathId, false)), ConformanceModuleSource.MathId, false, []);
        var disabled = await Snapshot();
        await Error(await Patch(ConformanceModuleSource.StatisticsId, true), HttpStatusCode.Conflict, ModuleEndpoints.DependencyConflict);
        Assert.Equal(disabled, await Snapshot());
        var page = await Body(await SendAsync(HttpMethod.Get, ModuleEndpoints.Path));
        Assert.All(page.GetProperty("items").EnumerateArray().Where(item => ownedModules.Contains(item.GetProperty("id").GetString()!)),
            item => Assert.False(item.GetProperty("enabled").GetBoolean()));
        for (int repetition = 0; repetition < 2; repetition++)
            AssertModule(await Body(await Patch(ConformanceModuleSource.MathId, true)), ConformanceModuleSource.MathId, true, []);
        for (int repetition = 0; repetition < 2; repetition++)
            AssertModule(await Body(await Patch(ConformanceModuleSource.StatisticsId, true)), ConformanceModuleSource.StatisticsId, true, [ConformanceModuleSource.MathId]);
        Assert.Equal(original, await Snapshot());
    }

    [Fact]
    public async Task UnknownCasingAndPersistedUnavailableIdsHaveExact404AndRetainHistoricalState()
    {
        foreach (string id in new[] { "unknown-" + Guid.NewGuid().ToString("N"), retired, ConformanceModuleSource.MathId.ToUpperInvariant() })
            await Error(await Patch(id, true), HttpStatusCode.NotFound, ModuleEndpoints.NotFound);
        Assert.False(await application.Services.GetRequiredService<IModuleStateStore>().FindAsync(retired));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("\"scalar\"")]
    [InlineData("{}")]
    [InlineData("{\"extra\":true}")]
    [InlineData("{\"enabled\":null}")]
    [InlineData("{\"enabled\":\"true\"}")]
    [InlineData("{\"enabled\":1}")]
    [InlineData("{\"Enabled\":true}")]
    [InlineData("{\"enabled\":false,\"extra\":true}")]
    [InlineData("{\"enabled\":true,\"enabled\":false}")]
    [InlineData("{\"enabled\":true} {}")]
    public async Task MalformedOrNonClosedBodyNeverMutatesState(string body)
    {
        var original = await Snapshot();
        await Error(await SendAsync(HttpMethod.Patch, ModuleEndpoints.Path + "/" + ConformanceModuleSource.MathId, body),
            HttpStatusCode.BadRequest, ModuleEndpoints.InvalidRequest);
        Assert.Equal(original, await Snapshot());
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-fixture-token")]
    [InlineData("non-admin")]
    public async Task SecurityPrecedesInvalidBodyQueryAndUnknownLookup(string identity)
    {
        string token = identity == "non-admin" ? userToken : identity;
        var expected = identity == "non-admin" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized;
        string code = expected == HttpStatusCode.Forbidden ? "forbidden" : "unauthorized";
        var original = await Snapshot();
        await Error(await SendAsync(HttpMethod.Get, ModuleEndpoints.Path + "?limit=invalid", token: token), expected, code);
        await Error(await SendAsync(HttpMethod.Patch, ModuleEndpoints.Path + "/unknown-fixture", "{", token), expected, code);
        await Error(await SendAsync(HttpMethod.Patch, ModuleEndpoints.Path + "/unknown-fixture", "{\"enabled\":true}", token), expected, code);
        Assert.Equal(original, await Snapshot());
    }

    [Fact]
    public async Task EmptyCompositionHasClosedPageAndNeverExposesRetainedRows()
    {
        using var empty = WithModules([]);
        using var emptyClient = empty.CreateClient();
        emptyClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(BearerScheme, adminToken);
        var response = await emptyClient.GetAsync(ModuleEndpoints.Path);
        AssertHeaders(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await Body(response);
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(0, page.GetProperty("total").GetInt32());
        Assert.Equal(PageFields, page.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.False(await empty.Services.GetRequiredService<IModuleStateStore>().FindAsync(retired));
    }

    [Fact]
    public async Task MultipleDependenciesUseOrdinalWireOrderBeforePaging()
    {
        string prefix = "native-module-order-" + Guid.NewGuid().ToString("N");
        string a = prefix + "-A", z = prefix + "-Z", lower = prefix + "-a";
        ownedModules.AddRange([a, z, lower]);
        using var ordered = WithModules([new DescriptorModule(lower, [z, a]), new DescriptorModule(z, []), new DescriptorModule(a, [])]);
        using var orderedClient = ordered.CreateClient();
        orderedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(BearerScheme, adminToken);
        var response = await orderedClient.GetAsync(ModuleEndpoints.Path + "?offset=2&limit=1");
        AssertHeaders(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await Body(response);
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        AssertModule(Assert.Single(page.GetProperty("items").EnumerateArray()), lower, true, [a, z]);
    }

    [Theory]
    [InlineData("en", "Internal server error")]
    [InlineData("es", "Error interno del servidor")]
    public async Task MissingStateAndUnexpectedStorageFailuresAreNotFalse404Or409(string language, string title)
    {
        await using var context = await application.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        await context.ModuleStates.Where(row => row.ModuleId == ConformanceModuleSource.StatisticsId).ExecuteDeleteAsync();
        using var request = new HttpRequestMessage(HttpMethod.Patch, ModuleEndpoints.Path + "/" + ConformanceModuleSource.MathId);
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, adminToken);
        request.Headers.AcceptLanguage.ParseAdd(language);
        request.Content = JsonContent.Create(new { enabled = false });
        var missing = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, missing.StatusCode);
        Assert.Equal(title, (await Body(missing)).GetProperty("title").GetString());
        Assert.True(await states.IsEnabledAsync(ConformanceModuleSource.MathId));
        using var broken = application.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IModuleStateStore>(new UnavailableStore())));
        using var brokenClient = broken.CreateClient();
        brokenClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(BearerScheme, adminToken);
        brokenClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        var failure = await brokenClient.PatchAsJsonAsync(ModuleEndpoints.Path + "/" + ConformanceModuleSource.MathId, new { enabled = false });
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.Equal(title, (await Body(failure)).GetProperty("title").GetString());
    }

    private WebApplicationFactory<Program> WithModules(IAlxarafeModule[] modules) =>
        application.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ModuleRegistry>();
            services.RemoveAll<ModuleRuntime>();
            new ModuleBuilder(services).AddSource(new Source(modules)).Build();
        }));

    private async Task<bool[]> Snapshot() =>
        [await states.IsEnabledAsync(ConformanceModuleSource.MathId), await states.IsEnabledAsync(ConformanceModuleSource.StatisticsId)];
    private Task<HttpResponseMessage> Patch(string id, bool enabled) =>
        SendAsync(HttpMethod.Patch, ModuleEndpoints.Path + "/" + id, JsonSerializer.Serialize(new { enabled }));
    private async Task<string> LoginAsync(string email)
    {
        var response = await client.PostAsJsonAsync(LoginPath, new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Body(response)).GetProperty("accessToken").GetString()!;
    }
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token != "") request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token ?? adminToken);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, MediaTypeNames.Application.Json);
        var response = await client.SendAsync(request);
        AssertHeaders(response);
        if (response.StatusCode != HttpStatusCode.Unauthorized) Assert.Empty(response.Headers.WwwAuthenticate);
        return response;
    }
    private static Task<JsonElement> Body(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();
    private static void AssertHeaders(HttpResponseMessage response)
    {
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
    private static void AssertModule(JsonElement module, string id, bool enabled, string[] dependencies)
    {
        Assert.Equal(ModuleFields, module.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(id, module.GetProperty("id").GetString());
        Assert.Equal(ModuleApi.CurrentVersion, module.GetProperty("moduleApiVersion").GetInt32());
        Assert.Equal(enabled, module.GetProperty("enabled").GetBoolean());
        Assert.Equal(dependencies, module.GetProperty("dependencies").EnumerateArray().Select(item => item.GetString()));
    }
    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var property = Assert.Single((await Body(response)).EnumerateObject());
        Assert.Equal("code", property.Name);
        Assert.Equal(code, property.Value.GetString());
        if (status == HttpStatusCode.Unauthorized)
            Assert.Equal(BearerScheme, Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        else Assert.Empty(response.Headers.WwwAuthenticate);
    }
    private sealed record Source(IAlxarafeModule[] Modules) : IModuleSource
    {
        public IEnumerable<IAlxarafeModule> GetModules() => Modules;
    }
    private sealed class DescriptorModule(string id, string[] dependencies) : IAlxarafeModule
    {
        public ModuleDescriptor Descriptor { get; } = new(id, ModuleApi.CurrentVersion, dependencies);
        public void ConfigureServices(IServiceCollection services) { }
        public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class UnavailableStore : IModuleStateStore
    {
        public Task<bool?> FindAsync(string moduleId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Native storage-failure fixture.");
        public Task UpdateAsync(Action<IDictionary<string, bool>> change, CancellationToken cancellationToken = default) =>
            Task.CompletedTask; // Permit startup; requests fail on the persisted read.
    }
}
