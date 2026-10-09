using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class LoginIdentityTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task LoginWhenPasswordFailsIdentityCountsFailureAndHonorsLockout()
    {
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
        var user = await CreateUserAsync(users);
        var failed = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "incorrect" });
        Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Entry(user).ReloadAsync();
        Assert.Equal(1, await users.GetAccessFailedCountAsync(user));

        Assert.True((await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5))).Succeeded);
        var locked = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "Password123" });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(await failed.Content.ReadAsStringAsync(), await locked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LoginWhenIdentityRequiresTwoFactorDoesNotIssuePasswordOnlyToken()
    {
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
        var user = await CreateUserAsync(users);
        Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        Assert.True(await scope.ServiceProvider.GetRequiredService<SignInManager<AlxarafeUser>>().IsTwoFactorEnabledAsync(user));

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "Password123" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_credentials", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("accessToken", out _));
    }

    private static async Task<AlxarafeUser> CreateUserAsync(UserManager<AlxarafeUser> users)
    {
        var email = $"identity-{Guid.NewGuid():N}@example.test";
        var user = new AlxarafeUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, "Password123")).Succeeded);
        return user;
    }
}
