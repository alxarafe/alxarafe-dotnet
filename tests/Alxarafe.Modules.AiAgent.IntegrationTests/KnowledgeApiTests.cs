using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Modularity;
using Alxarafe.Modules.AiAgent.Application;
using Alxarafe.Modules.AiAgent.Domain;
using Alxarafe.Modules.AiAgent.Infrastructure;
using Alxarafe.Modules.AiAgent.ModuleDefinition;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Alxarafe.Modules.AiAgent.IntegrationTests;

public sealed partial class KnowledgeApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static void RequireIsolation()
    {
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Testing" ||
            !UsesDatabase("ConnectionStrings__AiAgent", "alxarafe_ai_test") ||
            !UsesDatabase("ConnectionStrings__Security", "alxarafe_security_test"))
            throw new InvalidOperationException("AiAgent tests require Testing and isolated databases.");
    }

    private static bool UsesDatabase(string variable, string expected)
    {
        var connection = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrWhiteSpace(connection) && new NpgsqlConnectionStringBuilder(connection).Database == expected;
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task KnowledgeWithoutAuthenticationReturns401(string method)
    {
        RequireIsolation();
        using var client = factory.CreateClient();
        var response = method == "GET"
            ? await client.GetAsync($"/api/ai/knowledge/{Guid.NewGuid()}")
            : await client.PostAsJsonAsync("/api/ai/knowledge", new { question = "Question", answer = "Answer" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertStatusProblemAsync(response, 401);
    }

    [Fact]
    public async Task KnowledgeIsSharedAcrossInstallationForAuthorizedReaders()
    {
        RequireIsolation();
        using var writer = await CreateAuthenticatedClientAsync("ai-writer@example.test", "Creator_dev_only_123!");
        var response = await writer.PostAsJsonAsync("/api/ai/knowledge", new { question = "  A reusable question  ", answer = "  A reusable answer  " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<KnowledgeEntryDto>();
        Assert.NotNull(entry);
        Assert.Equal("A reusable question", entry.Question);
        Assert.Equal("A reusable answer", entry.Answer);
        Assert.Equal($"/api/ai/knowledge/{entry.Id}", response.Headers.Location?.OriginalString);

        using var reader = await CreateAuthenticatedClientAsync("ai-reader@example.test", "Reader_dev_only_123!");
        // Reader is a different user: read permission applies to the whole installation.
        var read = await reader.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(entry, await read.Content.ReadFromJsonAsync<KnowledgeEntryDto>());
        var forbidden = await reader.PostAsJsonAsync("/api/ai/knowledge", new { question = "Denied", answer = "Denied" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await AssertStatusProblemAsync(forbidden, 403);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiAgentDbContext>();
        var stored = await db.Knowledge.AsNoTracking().SingleAsync(row => row.Id == entry.Id);
        Assert.Equal(entry.Question, stored.Question);
        Assert.Equal(entry.Answer, stored.Answer);
    }

    [Fact]
    public async Task RegisteredUserWithoutPermissionsCannotReadOrWrite()
    {
        RequireIsolation();
        using var client = factory.CreateClient();
        var email = $"ai-test-{Guid.NewGuid():N}@example.test";
        var registration = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Password123" });
        registration.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, email, "Password123"));
        var read = await client.GetAsync($"/api/ai/knowledge/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        await AssertStatusProblemAsync(read, 403);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/ai/knowledge", new { question = "Denied", answer = "Denied" })).StatusCode);
    }

    [Theory]
    [InlineData("es", "No se encontró la entrada de conocimiento solicitada.")]
    [InlineData("en", "The requested knowledge entry was not found.")]
    [InlineData(null, "No se encontró la entrada de conocimiento solicitada.")]
    public async Task MissingKnowledgeReturnsLocalizedProblem(string? language, string title)
    {
        RequireIsolation();
        using var client = await CreateAuthenticatedClientAsync("ai-reader@example.test", "Reader_dev_only_123!");
        if (language is not null) client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        var response = await client.GetAsync($"/api/ai/knowledge/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("ai.knowledge_not_found", problem.GetProperty("code").GetString());
        Assert.Equal("https://alxarafe.dev/problems/ai.knowledge_not_found", problem.GetProperty("type").GetString());
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("", "Answer")]
    [InlineData("Question", "  ")]
    [InlineData(null, "Answer")]
    [InlineData("Question", null)]
    public async Task InvalidKnowledgeReturns400WithoutPersistence(string? question, string? answer)
    {
        RequireIsolation();
        using var client = await CreateAuthenticatedClientAsync("ai-writer@example.test", "Creator_dev_only_123!");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiAgentDbContext>();
        var before = await db.Knowledge.CountAsync();
        var response = await client.PostAsJsonAsync("/api/ai/knowledge", new { question, answer });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_error", problem.GetProperty("code").GetString());
        Assert.Equal(before, await db.Knowledge.CountAsync());
    }

    [Fact]
    public async Task MigrationsMatchModelAndAreIdempotent()
    {
        RequireIsolation();
        // Starting this fixture applies the module's migration.
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiAgentDbContext>();
        Assert.Equal(["202610060001_InitialKnowledge", "20261008000100_KnowledgeTextLimits"], await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        await db.Database.MigrateAsync();
        Assert.Equal(["202610060001_InitialKnowledge", "20261008000100_KnowledgeTextLimits"], await db.Database.GetAppliedMigrationsAsync());
    }

    [Theory]
    [InlineData("a")]
    [InlineData("\U0001F600")]
    public async Task ExactScalarLimitsAreNormalizedPersistedAndShared(string scalar)
    {
        RequireIsolation();
        using var writer = await CreateAuthenticatedClientAsync("ai-writer@example.test", "Creator_dev_only_123!");
        var question = string.Concat(Enumerable.Repeat(scalar, KnowledgeEntry.QuestionMaxLength));
        var answer = string.Concat(Enumerable.Repeat(scalar, KnowledgeEntry.AnswerMaxLength));
        var response = await writer.PostAsJsonAsync("/api/ai/knowledge", new { question = $"  {question}  ", answer = $"\t{answer}\n" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<KnowledgeEntryDto>();
        Assert.NotNull(entry);
        Assert.Equal(question, entry.Question);
        Assert.Equal(answer, entry.Answer);
        using var reader = await CreateAuthenticatedClientAsync("ai-reader@example.test", "Reader_dev_only_123!");
        var read = await reader.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(entry, await read.Content.ReadFromJsonAsync<KnowledgeEntryDto>());
        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AiAgentDbContext>().Knowledge.AsNoTracking().SingleAsync(row => row.Id == entry.Id);
        Assert.Equal(question, stored.Question);
        Assert.Equal(answer, stored.Answer);
    }

    [Theory]
    [InlineData(true, "a", "es")]
    [InlineData(true, "a", "en")]
    [InlineData(false, "a", "es")]
    [InlineData(false, "a", "en")]
    [InlineData(true, "\U0001F600", "es")]
    [InlineData(true, "\U0001F600", "en")]
    [InlineData(false, "\U0001F600", "es")]
    [InlineData(false, "\U0001F600", "en")]
    public async Task ExcessTextReturnsLocalizedValidationProblemWithoutPersistence(bool questionIsTooLong, string scalar, string language)
    {
        RequireIsolation();
        using var client = await CreateAuthenticatedClientAsync("ai-writer@example.test", "Creator_dev_only_123!");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiAgentDbContext>();
        var before = await db.Knowledge.CountAsync();
        var text = string.Concat(Enumerable.Repeat(scalar, (questionIsTooLong ? KnowledgeEntry.QuestionMaxLength : KnowledgeEntry.AnswerMaxLength) + 1));
        var response = await client.PostAsJsonAsync("/api/ai/knowledge", new
        {
            question = questionIsTooLong ? text : "Question",
            answer = questionIsTooLong ? "Answer" : text
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("validation_error", problem.GetProperty("code").GetString());
        Assert.Equal("https://alxarafe.dev/problems/validation_error", problem.GetProperty("type").GetString());
        Assert.Equal(language == "es" ? "La solicitud contiene datos no válidos." : "The request contains invalid data.", problem.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal(before, await db.Knowledge.CountAsync());
    }

    [Theory]
    [InlineData(true, "es")]
    [InlineData(true, "en")]
    [InlineData(false, "es")]
    [InlineData(false, "en")]
    public async Task EscapedNullScalarReachesDomainValidationWithoutLeakingOrPersistingText(bool inQuestion, string language)
    {
        RequireIsolation();
        using var client = await CreateAuthenticatedClientAsync("ai-writer@example.test", "Creator_dev_only_123!");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        var json = inQuestion
            ? "{\"question\":\"nul-before\\u0000nul-after\",\"answer\":\"Valid answer\"}"
            : "{\"question\":\"Valid question\",\"answer\":\"nul-before\\u0000nul-after\"}";
        // This is valid JSON containing an escaped scalar, not a parser-error request.
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("nul-before\u0000nul-after", parsed.RootElement.GetProperty(inQuestion ? "question" : "answer").GetString());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiAgentDbContext>();
        var before = await db.Knowledge.CountAsync();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/ai/knowledge", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("validation_error", problem.GetProperty("code").GetString());
        Assert.Equal("https://alxarafe.dev/problems/validation_error", problem.GetProperty("type").GetString());
        Assert.Equal(language == "es" ? "La solicitud contiene datos no válidos." : "The request contains invalid data.", problem.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("nul-before", body);
        Assert.DoesNotContain("nul-after", body);
        Assert.Equal(before, await db.Knowledge.CountAsync());
    }

    [Fact]
    public async Task WriteOnlyUserCannotReadAnExistingEntryEvenWhenTheyCreatedIt()
    {
        RequireIsolation();
        using var bootstrap = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Alxarafe.Security.EntityFrameworkCore.AlxarafeUser>>();
        var email = $"ai-write-only-{Guid.NewGuid():N}@example.test";
        var user = new Alxarafe.Security.EntityFrameworkCore.AlxarafeUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        Assert.True((await users.CreateAsync(user, "Creator_dev_only_123!")).Succeeded);
        Assert.True((await users.AddClaimAsync(user, new System.Security.Claims.Claim(
            Alxarafe.Security.PermissionClaim.Type, "ai.knowledge.write"))).Succeeded);
        var claim = Assert.Single(await users.GetClaimsAsync(user));
        Assert.Equal(Alxarafe.Security.PermissionClaim.Type, claim.Type);
        Assert.Equal("ai.knowledge.write", claim.Value);
        using var writer = await CreateAuthenticatedClientAsync(email, "Creator_dev_only_123!");
        var created = await writer.PostAsJsonAsync("/api/ai/knowledge", new
        {
            question = "Write-only confidential question",
            answer = "Write-only confidential answer"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var entry = await created.Content.ReadFromJsonAsync<KnowledgeEntryDto>();
        Assert.NotNull(entry);
        Assert.True(await scope.ServiceProvider.GetRequiredService<AiAgentDbContext>().Knowledge.AnyAsync(row => row.Id == entry.Id));
        using var reader = await CreateAuthenticatedClientAsync("ai-reader@example.test", "Reader_dev_only_123!");
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(created.Headers.Location)).StatusCode);
        var denied = await writer.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await AssertStatusProblemAsync(denied, 403);
        var body = await denied.Content.ReadAsStringAsync();
        Assert.DoesNotContain(entry.Question, body);
        Assert.DoesNotContain(entry.Answer, body);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, email, password));
        return client;
    }

    [Fact]
    public async Task ModuleCanInitializeAndPersistWithoutSiblingOrIdentityInfrastructure()
    {
        RequireIsolation();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Services.AddAlxarafeModules(typeof(AiAgentModule).Assembly);
        using var host = builder.Build();
        var runtime = host.Services.GetRequiredService<ModuleRuntime>();
        Assert.Equal("AiAgent", Assert.Single(runtime.Modules).Id);
        await runtime.InitializeAsync(host.Services);
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateKnowledgeEntryHandler>();
        var entry = await handler.HandleAsync(new CreateKnowledgeEntryCommand("Independent module", "Independent answer"));
        var read = await scope.ServiceProvider.GetRequiredService<GetKnowledgeEntryByIdHandler>().HandleAsync(entry.Id);
        Assert.Equal(entry, read);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
    }

    private sealed record TokenResponse(string AccessToken);

    private static async Task AssertStatusProblemAsync(HttpResponseMessage response, int status)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
