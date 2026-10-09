using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class LoginOpenApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task LoginMetadataDescribesPublicStrictJsonExchange()
    {
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var login = document.GetProperty("paths").GetProperty("/api/auth/login").GetProperty("post");
        Assert.False(login.TryGetProperty("security", out var security) && security.GetArrayLength() > 0);
        Assert.True(login.GetProperty("requestBody").GetProperty("required").GetBoolean());
        var request = Schema(document, login.GetProperty("requestBody"));
        AssertClosedObject(request, ["email", "password"]);
        foreach (var name in new[] { "email", "password" })
        {
            var property = request.GetProperty("properties").GetProperty(name);
            Assert.Equal("string", property.GetProperty("type").GetString());
            Assert.Equal(1, property.GetProperty("minLength").GetInt32());
            Assert.False(property.TryGetProperty("format", out _));
        }

        var responses = login.GetProperty("responses");
        var success = Schema(document, responses.GetProperty("200"));
        AssertClosedObject(success, ["accessToken"]);
        Assert.Equal("string", success.GetProperty("properties").GetProperty("accessToken").GetProperty("type").GetString());
        AssertClosedObject(Schema(document, responses.GetProperty("400")), ["code"]);
        AssertClosedObject(Schema(document, responses.GetProperty("401")), ["code"]);
    }

    private static JsonElement Schema(JsonElement document, JsonElement message)
    {
        var schema = message.GetProperty("content").GetProperty("application/json").GetProperty("schema");
        if (schema.TryGetProperty("$ref", out var reference))
            return document.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1]);
        return schema;
    }

    private static void AssertClosedObject(JsonElement schema, string[] fields)
    {
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(fields.Order(), schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(fields.Order(), schema.GetProperty("required").EnumerateArray().Select(field => field.GetString()).Order());
    }
}
