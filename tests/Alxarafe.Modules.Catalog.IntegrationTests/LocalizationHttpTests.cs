using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Alxarafe.Modules.Catalog.IntegrationTests;

public sealed partial class CatalogApiTests
{
    private static readonly string[] _problemShape = ["code", "status", "title", "traceId", "type"];
    [Theory]
    [InlineData("en", "The field sku must contain between 1 and 50 characters.")]
    [InlineData("es", "El campo sku debe contener entre 1 y 50 caracteres.")]
    [InlineData(null, "The field sku must contain between 1 and 50 characters.")]
    [InlineData("fr", "The field sku must contain between 1 and 50 characters.")]
    [InlineData("es-ES", "El campo sku debe contener entre 1 y 50 caracteres.")]
    [InlineData("en-US", "The field sku must contain between 1 and 50 characters.")]
    public async Task ExistingValidationUsesCatalogResourcesAndCanonicalFieldNames(string? language, string title)
    {
        RequireIsolation();
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "creator@example.test",
            password = "Creator_dev_only_123!"
        });
        login.EnsureSuccessStatusCode();
        var token = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("accessToken").GetString());
        if (language is not null) client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        var response = await client.PostAsJsonAsync("/api/catalog/items", new { sku = "", name = "Valid item" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("validation_error", problem.GetProperty("code").GetString());
        Assert.Equal("https://alxarafe.dev/problems/validation_error", problem.GetProperty("type").GetString());
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.Equal(_problemShape,
            problem.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }
}
