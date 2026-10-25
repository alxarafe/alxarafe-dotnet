using Alxarafe.EntityFrameworkCore;
using Alxarafe.EntityFrameworkCore.Migrations;
using Alxarafe.Modules.Ai.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class TenancyMigrationTests(TenantApplicationFactory factory) : IClassFixture<TenantApplicationFactory>
{
    [Fact]
    public async Task LegacySettingsSurviveUpgradeAndRollbackWithoutManufacturingTenantAuthority()
    {
        const string legacyKey = "legacy-unmapped";
        using var http = factory.CreateClient();
        await using var original = await factory.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        var connection = new NpgsqlConnectionStringBuilder(original.Database.GetConnectionString());
        string schema = "tenancy_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        connection.SearchPath = schema;
        var options = new DbContextOptionsBuilder<PlatformDbContext>();
        PlatformStorage.Configure(options, connection.ConnectionString);
        try
        {
            await using var context = new PlatformDbContext(options.Options);
            var migrator = context.GetService<IMigrator>();
            string previous = context.GetService<IMigrationsIdGenerator>().GetName(TenantAiSettingsMigration.MigrationId);
            await migrator.MigrateAsync(previous);
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO tenant_ai_settings (tenant_id, provider_override, model_override) VALUES ({legacyKey}, {OllamaAiModelClient.ProviderName}, {"fixture-model"})");
            await context.Database.MigrateAsync();
            Assert.Empty(await context.Tenants.ToArrayAsync());
            Assert.Empty(await context.TenantMemberships.ToArrayAsync());
            var settings = await context.TenantAiSettings.SingleAsync();
            Assert.Equal(legacyKey, settings.TenantId);
            Assert.Equal("fixture-model", settings.ModelOverride);
            Assert.Null(settings.CanonicalTenantId);
            Assert.False(context.Database.HasPendingModelChanges());
            settings.CanonicalTenantId = "unknown-canonical-tenant";
            var invalid = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(invalid.InnerException).SqlState);
            context.ChangeTracker.Clear();
            context.Tenants.Add(new TenantRow { Id = "canonical-fixture", Key = "fixture", DisplayName = "Fixture", DatabaseReference = "fixture" });
            settings = await context.TenantAiSettings.SingleAsync();
            settings.CanonicalTenantId = "canonical-fixture";
            await context.SaveChangesAsync();
            context.TenantAiSettings.Add(new TenantAiSettingsRow { TenantId = "second-legacy-key", CanonicalTenantId = "canonical-fixture" });
            var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
            context.ChangeTracker.Clear();
            await migrator.MigrateAsync(previous);
            await context.Database.OpenConnectionAsync();
            await using var query = context.Database.GetDbConnection().CreateCommand();
            query.CommandText = "SELECT count(*) FROM tenant_ai_settings";
            Assert.Equal(1L, await query.ExecuteScalarAsync());
            await context.Database.MigrateAsync();
            Assert.Null((await context.TenantAiSettings.SingleAsync()).CanonicalTenantId);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
