using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

// Host tests are serialized at assembly level. These cases temporarily isolate
// CORE administrator claims in the disposable security database and restore them.
public sealed class UsersInvariantTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastEnabledAdministratorCannotBeDemotedOrDisabled(bool disable)
    {
        using var client = factory.CreateClient();
        await WithIsolatedAdmins(async (users, database, ids) =>
        {
            var admin = await users.CreateAsync(Email(), "Invariant_fixture_123", true);
            var disabled = await users.CreateAsync(Email(), "Invariant_fixture_123", true);
            ids.AddRange([admin!.Id, disabled!.Id]);
            Assert.Equal(UserUpdateOutcome.Updated, (await users.UpdateAsync(disabled.Id, false, null)).Outcome);
            var login = await client.PostAsJsonAsync("/api/auth/login", new { email = admin.Email, password = "Invariant_fixture_123" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await client.PatchAsJsonAsync("/api/users/" + admin.Id,
                disable ? new Dictionary<string, bool> { ["enabled"] = false } : new Dictionary<string, bool> { ["admin"] = false });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Single(error.EnumerateObject());
            Assert.Equal("last_admin", error.GetProperty("code").GetString());
            Assert.True((await users.FindAsync(admin.Id)) is { Enabled: true, Admin: true });
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ConcurrentStateChangesSerializeBeforeReadingAndPreserveOneAdmin(bool disableFirst, bool disableSecond)
    {
        using var client = factory.CreateClient();
        await WithIsolatedAdmins(async (users, database, ids) =>
        {
            var first = await users.CreateAsync(Email(), "Invariant_fixture_123", true);
            var second = await users.CreateAsync(Email(), "Invariant_fixture_123", true);
            ids.AddRange([first!.Id, second!.Id]);
            var connectionString = database.Database.GetConnectionString()!;
            var application = "users-race-" + Guid.NewGuid().ToString("N");
            await using var blocker = new NpgsqlConnection(connectionString);
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand("LOCK TABLE \"AspNetUsers\", \"AspNetUserClaims\" IN SHARE ROW EXCLUSIVE MODE", blocker, transaction))
                await command.ExecuteNonQueryAsync();

            async Task<UserUpdateResult> Change(Guid id, bool disable)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
                context.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = application }.ConnectionString);
                return await scope.ServiceProvider.GetRequiredService<UserAdministration>().UpdateAsync(id,
                    disable ? false : null, disable ? null : false);
            }

            var changes = new[] { Change(first.Id, disableFirst), Change(second.Id, disableSecond) };
            try
            {
                // Release only once PostgreSQL proves both workers are waiting on
                // the same real lock. Polling observes readiness, not race timing.
                var watch = Stopwatch.StartNew();
                int waiting = 0;
                while (watch.Elapsed < TimeSpan.FromSeconds(10))
                {
                    await using (var refresh = new NpgsqlCommand("SELECT pg_stat_clear_snapshot()", blocker, transaction))
                        await refresh.ExecuteNonQueryAsync();
                    await using var command = new NpgsqlCommand("SELECT count(*)::integer FROM pg_stat_activity WHERE application_name = @application AND wait_event_type = 'Lock'", blocker, transaction);
                    command.Parameters.AddWithValue("application", application);
                    waiting = (int)(await command.ExecuteScalarAsync())!;
                    if (waiting == 2) break;
                    await Task.Delay(10);
                }
                Assert.Equal(2, waiting);
            }
            finally { await transaction.RollbackAsync(); await Task.WhenAll(changes); }

            var results = await Task.WhenAll(changes);
            Assert.Single(results, result => result.Outcome == UserUpdateOutcome.Updated);
            Assert.Single(results, result => result.Outcome == UserUpdateOutcome.LastAdmin);
            var states = new List<CoreUserState?>();
            foreach (var id in ids) states.Add(await users.FindAsync(id));
            Assert.Single(states, state => state is { Enabled: true, Admin: true });
        });
    }

    private async Task WithIsolatedAdmins(Func<UserAdministration, SecurityDbContext, List<Guid>, Task> verify)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
        var original = await database.UserClaims.AsNoTracking().Where(claim => claim.ClaimType == CoreUserClaims.Admin).ToListAsync();
        await database.UserClaims.Where(claim => claim.ClaimType == CoreUserClaims.Admin).ExecuteDeleteAsync();
        var ids = new List<Guid>();
        try { await verify(scope.ServiceProvider.GetRequiredService<UserAdministration>(), database, ids); }
        finally
        {
            database.ChangeTracker.Clear();
            await database.Users.Where(user => ids.Contains(user.Id)).ExecuteDeleteAsync();
            database.UserClaims.AddRange(original);
            await database.SaveChangesAsync();
        }
    }

    private static string Email() => $"invariant-{Guid.NewGuid():N}@example.test";
}
