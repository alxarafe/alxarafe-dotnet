using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class PlatformApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static void RequireIsolation()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Security");
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Testing" ||
            string.IsNullOrWhiteSpace(connectionString) ||
            new NpgsqlConnectionStringBuilder(connectionString).Database != "alxarafe_security_test")
            throw new InvalidOperationException("Platform integration tests require Testing and the isolated security database.");
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

    private sealed record TokenResponse(string AccessToken);
}
