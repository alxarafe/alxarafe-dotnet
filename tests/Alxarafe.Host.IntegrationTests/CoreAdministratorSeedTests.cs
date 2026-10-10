using Alxarafe.Host;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class CoreAdministratorSeedTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task CurrentValidationAdminIsSeparateAndModuleUsersRemainNonAdmin()
    {
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
        var administration = scope.ServiceProvider.GetRequiredService<UserAdministration>();
        var admin = await users.FindByEmailAsync("admin@example.test");
        Assert.True((await administration.FindAsync(admin!.Id)) is { Enabled: true, Admin: true });
        Assert.DoesNotContain(await users.GetClaimsAsync(admin), claim => claim.Type == PermissionClaim.Type);
        foreach (var email in new[] { "reader@example.test", "creator@example.test" })
        {
            var moduleUser = await users.FindByEmailAsync(email);
            Assert.True((await administration.FindAsync(moduleUser!.Id)) is { Enabled: true, Admin: false });
            Assert.Contains(await users.GetClaimsAsync(moduleUser), claim => claim.Type == PermissionClaim.Type);
        }
    }

    [Fact]
    public async Task SeedWithoutExplicitPasswordDoesNotProvisionAnAdministrator()
    {
        using var client = factory.CreateClient();
        await using var provider = Provider("unconfigured-" + Guid.NewGuid() + "@example.test", null);
        await SecuritySeed.InitializeAsync(provider, factory.Services.GetRequiredService<IHostEnvironment>());
        await using var scope = provider.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.AnyAsync(user => user.Email!.StartsWith("unconfigured-")));
    }

    [Fact]
    public async Task ConcurrentAndRepeatedSeedCreatesOneAdminWithoutOverwritingItsHash()
    {
        using var client = factory.CreateClient();
        var email = "seed-" + Guid.NewGuid() + "@example.test";
        await using var provider = Provider(email, "Controlled_seed_123");
        var environment = factory.Services.GetRequiredService<IHostEnvironment>();
        try
        {
            await Task.WhenAll(SecuritySeed.InitializeAsync(provider, environment), SecuritySeed.InitializeAsync(provider, environment));
            await using var scope = provider.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
            var user = await database.Users.AsNoTracking().SingleAsync(user => user.Email == email);
            await SecuritySeed.InitializeAsync(provider, environment);
            var repeated = await database.Users.AsNoTracking().SingleAsync(user => user.Email == email);
            Assert.Equal(user.Id, repeated.Id);
            Assert.Equal(user.PasswordHash, repeated.PasswordHash);
            Assert.True((await scope.ServiceProvider.GetRequiredService<UserAdministration>().FindAsync(user.Id)) is { Enabled: true, Admin: true });
        }
        finally
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(user => user.Email == email).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ExistingNonAdminIsNeverEscalatedBySeed()
    {
        using var client = factory.CreateClient();
        var email = "seed-conflict-" + Guid.NewGuid() + "@example.test";
        await using var provider = Provider(email, "Controlled_seed_123");
        await using var scope = provider.CreateAsyncScope();
        var administration = scope.ServiceProvider.GetRequiredService<UserAdministration>();
        var user = await administration.CreateAsync(email, "Existing_fixture_123", false);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => SecuritySeed.InitializeAsync(provider, factory.Services.GetRequiredService<IHostEnvironment>()));
            Assert.True((await administration.FindAsync(user!.Id)) is { Enabled: true, Admin: false });
        }
        finally { await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(existing => existing.Email == email).ExecuteDeleteAsync(); }
    }

    private ServiceProvider Provider(string email, string? password)
    {
        using var scope = factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Database.GetConnectionString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SecurityDbContext>(options => options.UseNpgsql(connection));
        services.AddIdentityCore<AlxarafeUser>().AddEntityFrameworkStores<SecurityDbContext>();
        services.Configure<DevelopmentUsersOptions>(_ => { });
        services.AddScoped<UserAdministration>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecuritySeed:AdminEmail"] = email,
            ["SecuritySeed:AdminPassword"] = password
        }).Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task FreshAndLegacyIdentityEmailStorageUpgradePreservesExistingAccounts()
    {
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
        var reader = await database.Users.AsNoTracking().SingleAsync(user => user.Email == "reader@example.test");
        async Task AssertTextColumns()
        {
            var connection = database.Database.GetDbConnection();
            await database.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*)::integer FROM information_schema.columns WHERE table_name = 'AspNetUsers' AND column_name IN ('Email', 'NormalizedEmail') AND data_type = 'text'";
            Assert.Equal(2, (int)(await command.ExecuteScalarAsync())!);
        }
        await AssertTextColumns();
        // Reproduce the previous real schema in this disposable database.
        await database.Database.ExecuteSqlRawAsync("ALTER TABLE \"AspNetUsers\" ALTER COLUMN \"Email\" TYPE varchar(256), ALTER COLUMN \"NormalizedEmail\" TYPE varchar(256)");
        try
        {
            await SecuritySchema.UpgradeEmailStorageAsync(database);
            await SecuritySchema.UpgradeEmailStorageAsync(database);
            await AssertTextColumns();
            var preserved = await database.Users.AsNoTracking().SingleAsync(user => user.Id == reader.Id);
            Assert.Equal(reader.Email, preserved.Email);
            Assert.Equal(reader.PasswordHash, preserved.PasswordHash);
            Assert.True((await scope.ServiceProvider.GetRequiredService<UserAdministration>().FindAsync(reader.Id)) is { Enabled: true, Admin: false });
        }
        finally { await SecuritySchema.UpgradeEmailStorageAsync(database); }
    }
}
