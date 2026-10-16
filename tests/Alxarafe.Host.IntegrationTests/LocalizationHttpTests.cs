using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class LocalizationHttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string RegisterPath = "/api/auth/register";
    private static readonly string[] _identityShape = ["errors", "status", "title", "traceId", "type"];

    [Theory]
    [InlineData("en", "The request contains invalid data.")]
    [InlineData("es", "La solicitud contiene datos no válidos.")]
    [InlineData(null, "The request contains invalid data.")]
    [InlineData("fr", "The request contains invalid data.")]
    [InlineData("es-ES", "La solicitud contiene datos no válidos.")]
    [InlineData("en-US", "The request contains invalid data.")]
    public async Task TransversalValidationProblemUsesCoreResourcesWithStableCode(string? language, string title)
    {
        using var application = RuntimeProbe.Configure(factory);
        using var client = application.CreateClient();
        if (language is not null) client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        var response = await client.GetAsync(RuntimeProbe.InvalidPath);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Alxarafe.AspNetCore.CoreMessages.ValidationCode, body.GetProperty("code").GetString());
        Assert.Equal(title, body.GetProperty("title").GetString());
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    [Theory]
    [InlineData("en", "Unauthorized")]
    [InlineData("es", "No autorizado")]
    [InlineData(null, "Unauthorized")]
    [InlineData("fr", "Unauthorized")]
    public async Task NativeHttpProblemTitlesUseCoreResources(string? language, string title)
    {
        using var application = RuntimeProbe.Configure(factory);
        using var client = application.CreateClient();
        if (language is not null) client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        var response = await client.GetAsync(RuntimeProbe.Path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.False(problem.TryGetProperty("code", out _));
    }

    [Theory]
    [InlineData("en", "One or more validation errors occurred.", "Passwords must be at least 8 characters.")]
    [InlineData("es", "Se produjeron uno o más errores de validación.", "Las contraseñas deben tener al menos 8 caracteres.")]
    [InlineData(null, "One or more validation errors occurred.", "Passwords must be at least 8 characters.")]
    [InlineData("fr", "One or more validation errors occurred.", "Passwords must be at least 8 characters.")]
    [InlineData("es-ES", "Se produjeron uno o más errores de validación.", "Las contraseñas deben tener al menos 8 caracteres.")]
    [InlineData("en-US", "One or more validation errors occurred.", "Passwords must be at least 8 characters.")]
    [InlineData("en;q=0.5,es;q=0.9", "Se produjeron uno o más errores de validación.", "Las contraseñas deben tener al menos 8 caracteres.")]
    public async Task IdentityValidationNegotiatesPerRequestWithStableCodesAndShape(string? language, string title, string message)
    {
        using var client = factory.CreateClient();
        if (language is not null) client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        var response = await client.PostAsJsonAsync(RegisterPath,
            new { email = $"i18n-{Guid.NewGuid():N}@example.test", password = "Abcd1!" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal(title, problem.GetProperty("title").GetString());
        var error = Assert.Single(problem.GetProperty("errors").EnumerateObject());
        Assert.Equal("PasswordTooShort", error.Name);
        Assert.Equal(message, Assert.Single(error.Value.EnumerateArray()).GetString());
        Assert.Equal(_identityShape,
            problem.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CultureComesOnlyFromTheHeaderAndDoesNotLeakToTheNextRequest()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Culture=c=es|uic=es");
        using var spanish = new HttpRequestMessage(HttpMethod.Post, RegisterPath)
        {
            Content = JsonContent.Create(new { email = $"i18n-{Guid.NewGuid():N}@example.test", password = "Abcd1!" })
        };
        spanish.Headers.AcceptLanguage.ParseAdd("es");
        var first = await (await client.SendAsync(spanish)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Se produjeron uno o más errores de validación.", first.GetProperty("title").GetString());
        var second = await (await client.PostAsJsonAsync(RegisterPath + "?culture=es&ui-culture=es&lang=es",
            new { email = $"i18n-{Guid.NewGuid():N}@example.test", password = "Abcd1!" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("One or more validation errors occurred.", second.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("en", "The email address {0} is already in use.")]
    [InlineData("es", "La dirección de correo {0} ya está en uso.")]
    public async Task DuplicateIdentityEmailInterpolatesACompleteSecurityMessage(string language, string template)
    {
        using var client = factory.CreateClient();
        var request = new { email = $"duplicate-i18n-{Guid.NewGuid():N}@example.test", password = "Password123" };
        (await client.PostAsJsonAsync(RegisterPath, request)).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        var response = await client.PostAsJsonAsync(RegisterPath, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(string.Format(System.Globalization.CultureInfo.InvariantCulture, template, request.email),
            problem.GetProperty("errors").GetProperty("DuplicateEmail")[0].GetString());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public async Task SharedCoreErrorsRemainClosedAndLanguageIndependent(string language)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        var response = await client.GetAsync("/api/modules");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        ContractErrors.Single(await response.Content.ReadFromJsonAsync<JsonElement>(), "unauthorized");
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }
}
