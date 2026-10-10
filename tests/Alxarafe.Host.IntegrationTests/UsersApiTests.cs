using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Alxarafe.Host.IntegrationTests;

public sealed class UsersApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly List<Guid> owned = [];
    private HttpClient client = null!;
    private string adminToken = "";
    private Guid adminId;
    private const string Password = "  密码😀abcdefghi  ";
    private static readonly string[] UserFields = ["admin", "email", "enabled", "id"];
    private static readonly string[] PageFields = ["items", "limit", "offset", "order", "total"];
    private static readonly string[] BearerChallenge = ["Bearer"];

    public async Task InitializeAsync()
    {
        client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserAdministration>().CreateAsync(Email(), Password, true);
        adminId = user!.Id;
        owned.Add(adminId);
        adminToken = await Login(user.Email, Password);
    }

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
        await database.Users.Where(user => owned.Contains(user.Id)).ExecuteDeleteAsync();
        client.Dispose();
    }

    [Fact]
    public async Task CurrentStateIsRefreshedAndCoreAdminNeverGrantsModulePermissions()
    {
        var me = await Send(HttpMethod.Get, "/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        AssertUser(await Body(me), adminId.ToString(), true, true);
        var email = Email();
        var created = await Create(email, Password, false);
        var id = created.GetProperty("id").GetString()!;
        var token = await Login(email, Password);
        AssertUser(await Body(await Send(HttpMethod.Get, "/api/users/" + id)), id, true, false);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/users/unknown?limit=abc", token: token)).StatusCode);
        await Error(await Send(HttpMethod.Get, "/api/users?limit=abc", token: token), 403, "forbidden");
        await Error(await Send(HttpMethod.Post, "/api/users", "{", token), 403, "forbidden");
        await Error(await Send(HttpMethod.Get, "/api/users/unparseable"), 404, "user_not_found");
        await Error(await Send(HttpMethod.Get, "/api/users/" + Guid.NewGuid()), 404, "user_not_found");
        await Error(await Send(HttpMethod.Post, "/api/users", JsonSerializer.Serialize(new { email, password = Password, admin = false })), 409, "email_conflict");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/catalog/items/" + Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/ai/knowledge/" + Guid.NewGuid())).StatusCode);

        AssertUser(await Body(await Send(HttpMethod.Patch, "/api/users/" + id, "{\"admin\":true}")), id, true, true);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/users", token: token)).StatusCode);
        AssertUser(await Body(await Send(HttpMethod.Get, "/api/auth/me", token: token)), id, true, true);
        await Send(HttpMethod.Patch, "/api/users/" + id, "{\"admin\":false,\"enabled\":false}");
        await Error(await Send(HttpMethod.Get, "/api/auth/me", token: token), 401, "unauthorized");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/catalog/items/" + Guid.NewGuid(), token: token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/ai/knowledge/" + Guid.NewGuid(), token: token)).StatusCode);
        var disabledLogin = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, disabledLogin.StatusCode);
        Assert.Equal("invalid_credentials", (await Body(disabledLogin)).GetProperty("code").GetString());
        await Send(HttpMethod.Patch, "/api/users/" + id, "{\"enabled\":true}");
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/auth/me", token: token)).StatusCode);
        await Error(await Send(HttpMethod.Get, "/api/users", token: token), 403, "forbidden");
        Assert.False(string.IsNullOrEmpty(await Login(email, Password)));

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
        var stored = await users.FindByIdAsync(id);
        Assert.NotEqual(Password, stored!.PasswordHash);
        Assert.True(users.PasswordHasher.VerifyHashedPassword(stored, stored.PasswordHash!, Password) != PasswordVerificationResult.Failed);
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public async Task SupplementaryUnicodePasswordBoundariesAreCodePoints(int count, bool valid)
    {
        var email = Email();
        var password = string.Concat(Enumerable.Repeat("😀", count));
        var response = await Send(HttpMethod.Post, "/api/users", JsonSerializer.Serialize(new { email, password, admin = false }));
        if (!valid) { await Error(response, 400, "invalid_request"); return; }
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        owned.Add(Guid.Parse((await Body(response)).GetProperty("id").GetString()!));
        Assert.False(string.IsNullOrEmpty(await Login(email, password)));
    }

    [Fact]
    public async Task ExactEmailIdentitiesDoNotBecomeAmbiguousIdentityLookups()
    {
        var email = Email();
        var upper = email.ToUpperInvariant();
        var first = await Create(email, Password, false);
        var second = await Create(upper, Password, false);
        Assert.NotEqual(first.GetProperty("id").GetString(), second.GetProperty("id").GetString());
        foreach (var value in new[] { first, second })
        {
            var token = await Login(value.GetProperty("email").GetString()!, Password);
            var me = await Body(await Send(HttpMethod.Get, "/api/auth/me", token: token));
            Assert.Equal(value.GetProperty("id").GetString(), me.GetProperty("id").GetString());
        }
        var duplicate = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Password123" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task NonemptyEmailHasNoUncontractualIdentityLengthLimit()
    {
        var email = new string('x', 300) + "-" + Guid.NewGuid() + "@example.test";
        var created = await Create(email, Password, false);
        var token = await Login(email, Password);
        var me = await Body(await Send(HttpMethod.Get, "/api/auth/me", token: token));
        Assert.Equal(created.GetProperty("id").GetString(), me.GetProperty("id").GetString());
        Assert.Equal(email, me.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ContractCreationHasNoEmailSyntaxOrPasswordCompositionRulesWhileRegisterKeepsItsPolicy()
    {
        var user = await Create(" no-email-syntax-" + Guid.NewGuid(), "abcdefghijkl", true);
        Assert.True(user.GetProperty("admin").GetBoolean());
        var email = Email();
        var denied = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "abcdefghijkl" });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Password123" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var token = await Login(email, "Password123");
        var state = await Body(await Send(HttpMethod.Get, "/api/auth/me", token: token));
        owned.Add(Guid.Parse(state.GetProperty("id").GetString()!));
        Assert.True(state.GetProperty("enabled").GetBoolean());
        Assert.False(state.GetProperty("admin").GetBoolean());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("{\"email\":\"x\",\"password\":\"abcdefghijkl\"}")]
    [InlineData("{\"email\":\"\",\"password\":\"abcdefghijkl\",\"admin\":false}")]
    [InlineData("{\"email\":3,\"password\":\"abcdefghijkl\",\"admin\":false}")]
    [InlineData("{\"email\":\"x\",\"password\":3,\"admin\":false}")]
    [InlineData("{\"email\":\"x\",\"password\":null,\"admin\":false}")]
    [InlineData("{\"email\":\"x\",\"password\":\"abcdefghijkl\",\"admin\":\"false\"}")]
    [InlineData("{\"email\":\"x\",\"password\":\"abcdefghijkl\",\"admin\":null}")]
    [InlineData("{\"email\":\"x\",\"password\":\"abcdefghijkl\",\"admin\":false,\"enabled\":true}")]
    [InlineData("{\"email\":\"x\",\"password\":\"abcdefghijkl\",\"admin\":false,\"admin\":true}")]
    public async Task InvalidCreationIsClosedContractError(string body) =>
        await Error(await Send(HttpMethod.Post, "/api/users", body), 400, "invalid_request");

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("{\"email\":\"x\"}")]
    [InlineData("{\"password\":\"abcdefghijkl\"}")]
    [InlineData("{\"id\":\"x\"}")]
    [InlineData("{\"admin\":null}")]
    [InlineData("{\"enabled\":\"false\"}")]
    [InlineData("{\"admin\":1}")]
    [InlineData("{\"enabled\":false,\"extra\":true}")]
    [InlineData("{\"admin\":false,\"admin\":true}")]
    public async Task InvalidPatchIsClosedContractError(string body) =>
        await Error(await Send(HttpMethod.Patch, "/api/users/" + adminId, body), 400, "invalid_request");

    [Theory]
    [InlineData("offset=-1")]
    [InlineData("offset=abc")]
    [InlineData("offset=1.5")]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=abc")]
    [InlineData("limit=1.5")]
    [InlineData("limit=")]
    public async Task InvalidPaginationIsClosedContractError(string query) =>
        await Error(await Send(HttpMethod.Get, "/api/users?" + query), 400, "invalid_request");

    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/users?limit=abc")]
    [InlineData("/api/users/unknown")]
    public async Task MissingAndInvalidBearerPrecedeParsing(string path)
    {
        await Error(await Send(HttpMethod.Get, path, token: ""), 401, "unauthorized");
        await Error(await Send(HttpMethod.Get, path, token: "not-a-bearer-token"), 401, "unauthorized");
    }

    [Fact]
    public async Task PaginationUsesStableDatabaseOrderingAndIndependentTotal()
    {
        await Create(Email(), Password, false);
        await Create(Email(), Password, false);
        var defaults = await Body(await Send(HttpMethod.Get, "/api/users"));
        Assert.Equal(PageFields, defaults.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(0, defaults.GetProperty("offset").GetInt64());
        Assert.Equal(50, defaults.GetProperty("limit").GetInt32());
        Assert.Equal("[{\"field\":\"id\",\"direction\":\"asc\"}]", defaults.GetProperty("order").GetRawText());
        var total = defaults.GetProperty("total").GetInt64();
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
        var expected = await database.Users.OrderBy(user => user.Id).Take(50).Select(user => user.Id.ToString()).ToListAsync();
        Assert.Equal(expected, defaults.GetProperty("items").EnumerateArray().Select(user => user.GetProperty("id").GetString()));
        foreach (var offset in new[] { 0, 1 })
        {
            var page = await Body(await Send(HttpMethod.Get, $"/api/users?offset={offset}&limit=1"));
            Assert.Equal(total, page.GetProperty("total").GetInt64());
            Assert.Equal(1, page.GetProperty("limit").GetInt32());
            Assert.Equal(expected[offset], page.GetProperty("items")[0].GetProperty("id").GetString());
        }
        var empty = await Body(await Send(HttpMethod.Get, "/api/users?offset=9223372036854775808"));
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal("9223372036854775808", empty.GetProperty("offset").GetRawText());
        Assert.Equal(total, empty.GetProperty("total").GetInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelfChangesAreAllowedWithAnotherAdministrator(bool disable)
    {
        await Send(HttpMethod.Patch, "/api/users/" + adminId, disable ? "{\"enabled\":false}" : "{\"admin\":false}");
        await Error(await Send(HttpMethod.Get, "/api/users"), disable ? 401 : 403, disable ? "unauthorized" : "forbidden");
    }

    [Fact]
    public async Task GeneratedOpenApiIncludesSharedUsersAndImplementationSpecificRegister()
    {
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/auth/register").TryGetProperty("post", out _));
        foreach (var path in new[] { "/api/auth/me", "/api/users", "/api/users/{id}" })
            Assert.NotEmpty(paths.GetProperty(path).GetProperty("get").GetProperty("security").EnumerateArray());
        Assert.True(paths.GetProperty("/api/users").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/users/{id}").TryGetProperty("patch", out _));
        var parameters = paths.GetProperty("/api/users").GetProperty("get").GetProperty("parameters");
        Assert.Equal(["offset", "limit"], parameters.EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()));
        Assert.Equal(50, parameters[1].GetProperty("schema").GetProperty("default").GetInt32());
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var offset = schemas.GetProperty("UserPage").GetProperty("properties").GetProperty("offset");
        if (offset.TryGetProperty("$ref", out var reference)) offset = schemas.GetProperty(reference.GetString()!.Split('/')[^1]);
        Assert.Equal("integer", offset.GetProperty("type").GetString());
        Assert.False(schemas.GetProperty("User").GetProperty("additionalProperties").GetBoolean());
        Assert.False(schemas.GetProperty("UserPage").GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task ExpiredIdentityTicketHasTheSameClosedUnauthorizedResponse()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
        var user = await users.FindByIdAsync(adminId.ToString());
        var principal = await scope.ServiceProvider.GetRequiredService<SignInManager<AlxarafeUser>>().CreateUserPrincipalAsync(user!);
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(IdentityConstants.BearerScheme);
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1) }, IdentityConstants.BearerScheme);
        var expired = options.BearerTokenProtector.Protect(ticket);
        await Error(await Send(HttpMethod.Get, "/api/auth/me", token: expired), 401, "unauthorized");
    }

    [Fact]
    public async Task AnonymousHealthWithAValidBearerNeverConsultsUserPersistence()
    {
        var blocker = new UnavailableDatabase();
        using var application = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<SecurityDbContext>(options => options.AddInterceptors(blocker))));
        using var healthClient = application.CreateClient();
        var email = (await Body(await Send(HttpMethod.Get, "/api/auth/me"))).GetProperty("email").GetString();
        var login = await healthClient.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        healthClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await Body(login)).GetProperty("accessToken").GetString());
        blocker.Active = true;
        var response = await healthClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, blocker.BlockedQueries);
    }

    private sealed class UnavailableDatabase : DbCommandInterceptor
    {
        public bool Active { get; set; }
        public int BlockedQueries { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Active) { BlockedQueries++; throw new InvalidOperationException("Persistence is unavailable in this native liveness test."); }
            return ValueTask.FromResult(result);
        }
    }

    private static string Email() => $"native-users-{Guid.NewGuid():N}@example.test";
    private static async Task<JsonElement> Body(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();
    private async Task<string> Login(string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Body(response)).GetProperty("accessToken").GetString()!;
    }
    private async Task<JsonElement> Create(string email, string password, bool admin)
    {
        var response = await Send(HttpMethod.Post, "/api/users", JsonSerializer.Serialize(new { email, password, admin }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Body(response);
        var id = body.GetProperty("id").GetString()!;
        owned.Add(Guid.Parse(id));
        AssertUser(body, id, true, admin);
        Assert.Equal(email, body.GetProperty("email").GetString());
        return body;
    }
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? adminToken);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);
        if (path.StartsWith("/api/users", StringComparison.Ordinal) || path == "/api/auth/me")
        {
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
        return response;
    }
    private static void AssertUser(JsonElement user, string id, bool enabled, bool admin)
    {
        Assert.Equal(UserFields, user.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(id, user.GetProperty("id").GetString());
        Assert.True(user.GetProperty("email").GetString()?.Length > 0);
        Assert.Equal(enabled, user.GetProperty("enabled").GetBoolean());
        Assert.Equal(admin, user.GetProperty("admin").GetBoolean());
    }
    private static async Task Error(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        var body = await Body(response);
        Assert.Single(body.EnumerateObject());
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(status == 401 ? BearerChallenge : [], response.Headers.WwwAuthenticate.Select(value => value.ToString()));
    }
}
