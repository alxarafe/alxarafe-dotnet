using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.EntityFrameworkCore.Migrations;
using Alxarafe.Modules.Ai.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class TenantAiSettingsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] PreviousMigrations = ["202610120001_ModuleStates"];

    [Fact]
    public async Task PlatformPersistsIndependentNullableOverridesWithUniqueCaseSensitiveTenantKeys()
    {
        using var http = factory.CreateClient();
        var contexts = factory.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>();
        string tenant = "tenant-settings-" + Guid.NewGuid().ToString("N");
        string otherTenant = tenant.ToUpperInvariant();
        try
        {
            await using (var context = await contexts.CreateDbContextAsync())
            {
                context.TenantAiSettings.AddRange(new TenantAiSettingsRow { TenantId = tenant, ProviderOverride = OllamaAiModelClient.ProviderName },
                    new TenantAiSettingsRow { TenantId = otherTenant, ModelOverride = EffectiveAiConfigurationTests.LocalModel });
                await context.SaveChangesAsync();
                Assert.False(context.Database.HasPendingModelChanges());
                Assert.Contains(TenantAiSettingsMigration.MigrationId, await context.Database.GetAppliedMigrationsAsync());
                var properties = context.Model.FindEntityType(typeof(TenantAiSettingsRow))!.GetProperties().Select(property => property.Name).Order();
                Assert.Equal(new[] { nameof(TenantAiSettingsRow.ModelOverride), nameof(TenantAiSettingsRow.ProviderOverride), nameof(TenantAiSettingsRow.TenantId) }, properties);
            }
            var store = factory.Services.GetRequiredService<ITenantAiSettingsStore>();
            var values = await Task.WhenAll(store.FindAsync(tenant), store.FindAsync(otherTenant));
            Assert.Equal(new TenantAiSettings(tenant, OllamaAiModelClient.ProviderName), values[0]);
            Assert.Equal(new TenantAiSettings(otherTenant, null, EffectiveAiConfigurationTests.LocalModel), values[1]);
            await using var duplicate = await contexts.CreateDbContextAsync();
            duplicate.TenantAiSettings.Add(new TenantAiSettingsRow { TenantId = tenant });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
            await using var blank = await contexts.CreateDbContextAsync();
            blank.TenantAiSettings.Add(new TenantAiSettingsRow { TenantId = " " });
            var invalid = await Assert.ThrowsAsync<DbUpdateException>(() => blank.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(invalid.InnerException).SqlState);
        }
        finally
        {
            await using var context = await contexts.CreateDbContextAsync();
            await context.TenantAiSettings.Where(row => row.TenantId == tenant || row.TenantId == otherTenant).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task UpgradeAndDowngradeKeepExistingPlatformStateAndFrozenMigrationHistory()
    {
        using var http = factory.CreateClient();
        await using var original = await factory.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        var connection = new NpgsqlConnectionStringBuilder(original.Database.GetConnectionString());
        string schema = "tenant_ai_upgrade_" + Guid.NewGuid().ToString("N");
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
            string previous = context.GetService<IMigrationsIdGenerator>().GetName("202610120001_ModuleStates");
            await migrator.MigrateAsync(previous);
            context.ModuleStates.Add(new ModuleStateRow { ModuleId = "retained-module", Enabled = true });
            await context.SaveChangesAsync();
            await context.Database.MigrateAsync();
            Assert.True((await context.ModuleStates.SingleAsync()).Enabled);
            Assert.Empty(await context.TenantAiSettings.ToArrayAsync());
            Assert.False(context.Database.HasPendingModelChanges());
            context.TenantAiSettings.Add(new TenantAiSettingsRow { TenantId = "synthetic-tenant" });
            await context.SaveChangesAsync();
            await migrator.MigrateAsync(previous);
            Assert.True((await context.ModuleStates.SingleAsync()).Enabled);
            Assert.Equal(PreviousMigrations, await context.Database.GetAppliedMigrationsAsync());
            await context.Database.MigrateAsync();
            Assert.Empty(await context.TenantAiSettings.AsNoTracking().ToArrayAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
