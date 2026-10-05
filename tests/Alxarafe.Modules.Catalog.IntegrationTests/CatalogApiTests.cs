using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Modules.Catalog.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace Alxarafe.Modules.Catalog.IntegrationTests;

public sealed class CatalogApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly bool IsIsolated =
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Testing" &&
        UsesDatabase("ConnectionStrings__Catalog", "alxarafe_test") &&
        UsesDatabase("ConnectionStrings__Security", "alxarafe_security_test");

    private static bool UsesDatabase(string variable, string database)
    {
        var connectionString = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrWhiteSpace(connectionString) &&
            string.Equals(new NpgsqlConnectionStringBuilder(connectionString).Database, database, StringComparison.Ordinal);
    }

    private static void RequireIsolation()
    {
        if (!IsIsolated)
            throw new InvalidOperationException("Integration tests require Testing and the two _test databases.");
    }

    [Fact]
    public async Task HealthEndpointIsAvailable()
    {
        RequireIsolation();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpointWithoutCredentialsReturns401()
    {
        RequireIsolation();
        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/catalog/items/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateWithoutCredentialsReturns401()
    {
        RequireIsolation();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/catalog/items", new { sku = "NO-AUTH", name = "No auth" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterAndLoginUseIdentityApiEndpoints()
    {
        RequireIsolation();
        using var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.test";
        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Password123" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.False(string.IsNullOrWhiteSpace(token?.AccessToken));
    }

    [Fact]
    public async Task AuthenticatedUserWithoutPermissionReturns403()
    {
        RequireIsolation();
        using var client = factory.CreateClient();
        var token = await RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/catalog/items", new { sku = $"NOPE-{Guid.NewGuid():N}"[..18], name = "Not allowed" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SeededCreatorCanCreateAndReadItem()
    {
        RequireIsolation();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, "creator@example.test", "Creator_dev_only_123!"));
        var create = await client.PostAsJsonAsync("/api/catalog/items", new { sku = $"TEST-{Guid.NewGuid():N}"[..20], name = "Integration item" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var item = await create.Content.ReadFromJsonAsync<ItemDto>();
        Assert.NotNull(item);
        var read = await client.GetAsync($"/api/catalog/items/{item!.Id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task MissingItemReturnsLocalizedProblemDetails()
    {
        RequireIsolation();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, "reader@example.test", "Reader_dev_only_123!"));
        var id = Guid.NewGuid();
        var spanish = await GetMissingAsync(client, id, "es");
        var english = await GetMissingAsync(client, id, "en");
        Assert.Equal(HttpStatusCode.NotFound, spanish.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, english.StatusCode);
        Assert.Equal("application/problem+json", spanish.Content.Headers.ContentType?.MediaType);
        var es = await spanish.Content.ReadFromJsonAsync<JsonElement>();
        var en = await english.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("catalog.item_not_found", es.GetProperty("code").GetString());
        Assert.Equal("catalog.item_not_found", en.GetProperty("code").GetString());
        Assert.NotEqual(es.GetProperty("title").GetString(), en.GetProperty("title").GetString());
    }

    private static async Task<HttpResponseMessage> GetMissingAsync(HttpClient client, Guid id, string language)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/catalog/items/{id}");
        request.Headers.AcceptLanguage.ParseAdd(language);
        return await client.SendAsync(request);
    }

    private static async Task<string> RegisterAndLoginAsync(HttpClient client)
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Password123" });
        register.EnsureSuccessStatusCode();
        return await LoginAsync(client, email, "Password123");
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
    }

    private sealed record TokenResponse(string AccessToken);
}
