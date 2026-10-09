using System.Net;
using System.Text.Json;
using Alxarafe.Modules.AiAgent.Application;
using Alxarafe.Modules.AiAgent.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Xunit;

namespace Alxarafe.Modules.AiAgent.IntegrationTests;

public sealed partial class KnowledgeApiTests
{
    private static readonly string[] _undocumentedPublicPaths = ["/health", "/openapi/v1.json"];
    private static readonly string[] _postStatuses = ["201", "400", "401", "403", "415"];
    private static readonly string[] _getStatuses = ["200", "401", "403", "404"];
    private static readonly string[] _requestRequired = ["question", "answer"];
    private static readonly string[] _responseRequired = ["id", "question", "answer"];
    [Fact]
    public async Task OpenApiDeclaresOpaqueHttpBearerWithoutScopesOrGlobalSecurity()
    {
        using var document = await ReadOpenApiAsync();
        var root = document.RootElement;
        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.False(scheme.TryGetProperty("bearerFormat", out _));
        Assert.False(scheme.TryGetProperty("flows", out _));
        Assert.False(HasSecurity(root));
    }

    [Theory]
    [InlineData("/api/ai/knowledge", "post")]
    [InlineData("/api/ai/knowledge/{id}", "get")]
    public async Task KnowledgeOpenApiRequiresBearer(string path, string method)
    {
        using var document = await ReadOpenApiAsync();
        var operation = Operation(document.RootElement, path, method);
        var requirement = Assert.Single(operation.GetProperty("security").EnumerateArray());
        var scheme = Assert.Single(requirement.EnumerateObject());
        Assert.Equal("Bearer", scheme.Name);
        Assert.Empty(scheme.Value.EnumerateArray());
    }

    [Theory]
    [InlineData("/health", "/health", "get")]
    [InlineData("/api/auth/register", "/api/auth/register", "post")]
    [InlineData("/api/auth/login", "/api/auth/login", "post")]
    [InlineData("/openapi/v1.json", "/openapi/{documentName}.json", "get")]
    public async Task PublicEndpointsHaveNoAuthorizationOrInheritedOpenApiSecurity(string path, string route, string method)
    {
        using var document = await ReadOpenApiAsync();
        Assert.False(HasSecurity(document.RootElement));
        var endpoint = Assert.Single(factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>(), endpoint => endpoint.RoutePattern.RawText == route);
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>());
        // Health checks and the document endpoint are not currently ApiExplorer operations.
        if (document.RootElement.GetProperty("paths").TryGetProperty(path, out var pathItem))
            Assert.False(HasSecurity(pathItem.GetProperty(method)));
        else
            Assert.Contains(path, _undocumentedPublicPaths);

        if (method == "get")
        {
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        }
    }

    [Fact]
    public async Task KnowledgePostOpenApiDescribesJsonRequestCreatedDtoAndActualProblems()
    {
        using var document = await ReadOpenApiAsync();
        var root = document.RootElement;
        var operation = Operation(root, "/api/ai/knowledge", "post");
        var request = operation.GetProperty("requestBody");
        Assert.True(request.GetProperty("required").GetBoolean());
        var schema = request.GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal("#/components/schemas/CreateKnowledgeEntryRequest", schema.GetProperty("$ref").GetString());
        AssertDtoProperties(root, schema, typeof(CreateKnowledgeEntryRequest));
        AssertRequiredProperties(root, schema, _requestRequired);
        var responses = operation.GetProperty("responses");
        Assert.Equal(_postStatuses, responses.EnumerateObject().Select(response => response.Name).Order());
        AssertKnowledgeResponse(root, responses.GetProperty("201"));
        foreach (var status in new[] { "400", "401", "403", "415" }) AssertProblem(root, responses.GetProperty(status));
    }

    [Fact]
    public async Task KnowledgeGetOpenApiDescribesRequiredUuidDtoAndActualProblems()
    {
        using var document = await ReadOpenApiAsync();
        var root = document.RootElement;
        var operation = Operation(root, "/api/ai/knowledge/{id}", "get");
        var parameter = Assert.Single(operation.GetProperty("parameters").EnumerateArray());
        Assert.Equal("id", parameter.GetProperty("name").GetString());
        Assert.Equal("path", parameter.GetProperty("in").GetString());
        Assert.True(parameter.GetProperty("required").GetBoolean());
        Assert.Equal("string", parameter.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("uuid", parameter.GetProperty("schema").GetProperty("format").GetString());
        var responses = operation.GetProperty("responses");
        Assert.Equal(_getStatuses, responses.EnumerateObject().Select(response => response.Name).Order());
        AssertKnowledgeResponse(root, responses.GetProperty("200"));
        foreach (var status in new[] { "401", "403", "404" }) AssertProblem(root, responses.GetProperty(status));
    }

    [Fact]
    public async Task GeneratedOpenApiIsValidAndEveryJsonReferenceResolves()
    {
        using var document = await ReadOpenApiAsync();
        Assert.Equal("3.1.1", document.RootElement.GetProperty("openapi").GetString());
        var parsed = OpenApiDocument.Parse(document.RootElement.GetRawText(), "json",
            new OpenApiReaderSettings { RuleSet = ValidationRuleSet.GetDefaultRuleSet() });
        Assert.NotNull(parsed.Document);
        Assert.NotNull(parsed.Diagnostic);
        Assert.Empty(parsed.Diagnostic.Errors);
        AssertReferencesResolve(document.RootElement, document.RootElement);
    }

    [Fact]
    public async Task UnsupportedKnowledgeContentTypeStillReturns415Problem()
    {
        RequireIsolation();
        using var client = await CreateAuthenticatedClientAsync("ai-writer@example.test", "Creator_dev_only_123!");
        using var content = new StringContent("question=Question&answer=Answer");
        var response = await client.PostAsync("/api/ai/knowledge", content);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(415, problem.RootElement.GetProperty("status").GetInt32());
    }

    private async Task<JsonDocument> ReadOpenApiAsync()
    {
        RequireIsolation();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static JsonElement Operation(JsonElement root, string path, string method) =>
        root.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static bool HasSecurity(JsonElement element) =>
        element.TryGetProperty("security", out var security) && security.GetArrayLength() > 0;

    private static void AssertKnowledgeResponse(JsonElement root, JsonElement response)
    {
        var schema = response.GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal("#/components/schemas/KnowledgeEntryDto", schema.GetProperty("$ref").GetString());
        AssertDtoProperties(root, schema, typeof(KnowledgeEntryDto));
        AssertRequiredProperties(root, schema, _responseRequired);
    }

    private static void AssertRequiredProperties(JsonElement root, JsonElement schema, IEnumerable<string> expected)
    {
        var required = ResolveSchema(root, schema).GetProperty("required").EnumerateArray()
            .Select(property => property.GetString()).ToHashSet(StringComparer.Ordinal);
        Assert.True(required.SetEquals(expected), "The schema must declare exactly the expected required properties.");
    }

    private static void AssertDtoProperties(JsonElement root, JsonElement schema, Type dto)
    {
        var properties = ResolveSchema(root, schema).GetProperty("properties");
        var publicProperties = dto.GetProperties();
        Assert.Equal(publicProperties.Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).Order(),
            properties.EnumerateObject().Select(property => property.Name).Order());
        foreach (var property in publicProperties)
        {
            var declared = properties.GetProperty(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
            Assert.Equal("string", declared.GetProperty("type").GetString());
            if (property.PropertyType == typeof(Guid)) Assert.Equal("uuid", declared.GetProperty("format").GetString());
            else
            {
                Assert.Equal(typeof(string), property.PropertyType);
                Assert.False(declared.TryGetProperty("format", out _));
            }
        }
    }

    private static void AssertProblem(JsonElement root, JsonElement response)
    {
        var schema = response.GetProperty("content").GetProperty("application/problem+json").GetProperty("schema");
        Assert.Equal("#/components/schemas/ProblemDetails", schema.GetProperty("$ref").GetString());
        var properties = ResolveSchema(root, schema).GetProperty("properties");
        Assert.Contains("integer", properties.GetProperty("status").GetProperty("type").EnumerateArray().Select(type => type.GetString()));
        foreach (var property in new[] { "type", "title", "detail", "instance" })
            Assert.Contains("string", properties.GetProperty(property).GetProperty("type").EnumerateArray().Select(type => type.GetString()));
    }

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema) =>
        schema.TryGetProperty("$ref", out var reference) ? ResolveReference(root, reference.GetString()!) : schema;

    private static JsonElement ResolveReference(JsonElement root, string reference)
    {
        Assert.StartsWith("#/", reference);
        var resolved = root;
        foreach (var segment in Uri.UnescapeDataString(reference[2..]).Split('/'))
        {
            var key = segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            resolved = resolved.ValueKind == JsonValueKind.Array ? resolved[int.Parse(key, System.Globalization.CultureInfo.InvariantCulture)] : resolved.GetProperty(key);
        }
        return resolved;
    }

    private static void AssertReferencesResolve(JsonElement root, JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (property.Name == "$ref") ResolveReference(root, property.Value.GetString()!);
                else AssertReferencesResolve(root, property.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) AssertReferencesResolve(root, item);
    }
}
