using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class HealthContractTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task HealthIsAnonymousJsonLivenessWithoutDependencyChecks()
    {
        Assert.Equal("Testing", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Security");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        Assert.Equal("alxarafe_security_test", new NpgsqlConnectionStringBuilder(connectionString).Database);
        var dependencyInvoked = false;
        using var application = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHealthChecks().AddCheck("unavailable-dependency", () =>
            {
                dependencyInvoked = true;
                return HealthCheckResult.Unhealthy();
            })));
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        Assert.Null(client.DefaultRequestHeaders.Authorization);

        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Object, body.RootElement.ValueKind);
        var property = Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal("status", property.Name);
        Assert.Equal(JsonValueKind.String, property.Value.ValueKind);
        Assert.Equal("ok", property.Value.GetString());
        Assert.False(dependencyInvoked);
    }
}
