using System.Diagnostics;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ModuleStateTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    [Fact]
    public async Task NewAvailableModulesDefaultEnabledAndRetiredStatesSurviveReconstruction()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.UpdateAsync(states => states.Add(fixture.Retired, false));
        await fixture.Service([]).SynchronizeAsync();
        Assert.False(await fixture.Store.FindAsync(fixture.Retired));
        var states = fixture.Service(fixture.Chain());
        await states.SynchronizeAsync();
        Assert.Equal(ModuleStateService.InitialEnabled, await states.IsEnabledAsync(fixture.A));
        Assert.True(await states.IsEnabledAsync(fixture.B));
        await states.RequireEnabledAsync(fixture.C);
        await states.DisableAsync(fixture.C);
        await states.DisableAsync(fixture.B);
        await states.DisableAsync(fixture.A);
        var reconstructed = fixture.Service(fixture.Chain());
        await reconstructed.SynchronizeAsync();
        Assert.False(await reconstructed.IsEnabledAsync(fixture.A));
        Assert.False(await reconstructed.IsEnabledAsync(fixture.B));
        Assert.False(await reconstructed.IsEnabledAsync(fixture.C));
        Assert.False(await fixture.Store.FindAsync(fixture.Retired));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reconstructed.EnableAsync(fixture.Retired));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reconstructed.DisableAsync(fixture.Retired));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reconstructed.IsEnabledAsync(fixture.Retired));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reconstructed.RequireEnabledAsync(fixture.Retired));
        var reinstalled = fixture.Service([Fixture.Module(fixture.Retired)]);
        await reinstalled.SynchronizeAsync();
        Assert.False(await reinstalled.IsEnabledAsync(fixture.Retired));
    }

    [Fact]
    public async Task DependencyChangesAreImmediateIdempotentAndNeverCascade()
    {
        await using var fixture = await Fixture.CreateAsync();
        var states = fixture.Service(fixture.Chain());
        await states.SynchronizeAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => states.DisableAsync(fixture.A));
        Assert.Contains(fixture.A, error.Message);
        Assert.Contains(fixture.B, error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => states.DisableAsync(fixture.B));
        await states.DisableAsync(fixture.C);
        await states.DisableAsync(fixture.B);
        await states.DisableAsync(fixture.A);
        await states.DisableAsync(fixture.A);
        var disabled = await Assert.ThrowsAsync<ModuleDisabledException>(() => states.RequireEnabledAsync(fixture.A));
        Assert.Equal(fixture.A, disabled.ModuleId);
        error = await Assert.ThrowsAsync<InvalidOperationException>(() => states.EnableAsync(fixture.B));
        Assert.Contains(fixture.B, error.Message);
        Assert.Contains(fixture.A, error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => states.EnableAsync(fixture.C));
        Assert.False(await states.IsEnabledAsync(fixture.B));
        await states.EnableAsync(fixture.A);
        await states.EnableAsync(fixture.A);
        await states.EnableAsync(fixture.B);
        await states.EnableAsync(fixture.C);
        Assert.True(await fixture.Service(fixture.Chain()).IsEnabledAsync(fixture.C));
        await states.RequireEnabledAsync(fixture.C);
    }

    [Fact]
    public async Task InvalidStartupStateRollsBackNewRowsWithoutRepairingExistingState()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.UpdateAsync(states =>
        {
            states.Add(fixture.A, false);
            states.Add(fixture.B, true);
        });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service(fixture.Chain()).SynchronizeAsync());
        Assert.Contains(fixture.A, error.Message);
        Assert.Contains(fixture.B, error.Message);
        Assert.False(await fixture.Store.FindAsync(fixture.A));
        Assert.True(await fixture.Store.FindAsync(fixture.B));
        Assert.Null(await fixture.Store.FindAsync(fixture.C));
    }

    [Fact]
    public async Task DisabledModulesStillInitializeInDependencyOrder()
    {
        await using var fixture = await Fixture.CreateAsync();
        var calls = new List<string>();
        var modules = fixture.Chain(calls);
        await fixture.Store.UpdateAsync(states =>
        {
            foreach (var module in modules) states.Add(module.Descriptor.Id, false);
        });
        await fixture.Service(modules).SynchronizeAsync();
        using var services = new ServiceCollection().BuildServiceProvider();
        await new ModuleRuntime(ModuleGraph.Order(modules)).InitializeAsync(services);
        Assert.Equal([fixture.A, fixture.B, fixture.C], calls);
        Assert.False(await fixture.Service(modules).IsEnabledAsync(fixture.C));
    }

    [Fact]
    public async Task KeysAreCaseSensitiveAndMigrationPreservesExistingData()
    {
        await using var fixture = await Fixture.CreateAsync();
        string lower = fixture.A.ToLowerInvariant();
        fixture.OwnedIds.Add(lower);
        var states = fixture.Service([Fixture.Module(fixture.A), Fixture.Module(lower)]);
        await states.SynchronizeAsync();
        await states.DisableAsync(fixture.A);
        Assert.False(await states.IsEnabledAsync(fixture.A));
        Assert.True(await states.IsEnabledAsync(lower));
        await using var context = fixture.CreateDbContext();
        Assert.False(context.Database.HasPendingModelChanges());
        await context.Database.MigrateAsync();
        Assert.True(await fixture.Store.FindAsync(lower));
    }

    [Fact]
    public async Task MigrationWorksOnCleanSchemaAndAlongsideExistingTablesAndHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        string schema = "module_migration_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        try
        {
            await using (var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection))
                await command.ExecuteNonQueryAsync();
            var isolatedConnection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { SearchPath = schema }.ConnectionString;
            await using (var context = Fixture.CreateDbContext(isolatedConnection))
            {
                await context.Database.MigrateAsync();
                Assert.Empty(await context.ModuleStates.ToListAsync());
            }
            await using (var command = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE; CREATE SCHEMA \"{schema}\"; CREATE TABLE \"{schema}\".existing_data (value text NOT NULL); INSERT INTO \"{schema}\".existing_data VALUES ('preserved'); CREATE TABLE \"{schema}\".\"__EFMigrationsHistory\" (\"MigrationId\" text PRIMARY KEY, \"ProductVersion\" text NOT NULL); INSERT INTO \"{schema}\".\"__EFMigrationsHistory\" VALUES ('existing', 'fixture')", connection))
                await command.ExecuteNonQueryAsync();
            await using (var context = Fixture.CreateDbContext(isolatedConnection))
                await context.Database.MigrateAsync();
            await using (var command = new NpgsqlCommand($"SELECT value FROM \"{schema}\".existing_data", connection))
                Assert.Equal("preserved", await command.ExecuteScalarAsync());
            await using (var command = new NpgsqlCommand($"SELECT \"MigrationId\" FROM \"{schema}\".\"__EFMigrationsHistory\"", connection))
                Assert.Equal("existing", await command.ExecuteScalarAsync());
        }
        finally
        {
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task ConcurrentEnableAndDisableWaitOnRealPostgresLockAndPreserveInvariant()
    {
        await using var fixture = await Fixture.CreateAsync();
        var states = fixture.Service(fixture.Chain());
        await states.SynchronizeAsync();
        await states.DisableAsync(fixture.C);
        await states.DisableAsync(fixture.B);
        string application = "module-race-" + Guid.NewGuid().ToString("N");
        var workerConnection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { ApplicationName = application }.ConnectionString;
        var workers = fixture.Service(fixture.Chain(), workerConnection);
        await using var blocker = new NpgsqlConnection(fixture.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("LOCK TABLE " + PlatformStorage.ModuleStatesTable + " IN SHARE ROW EXCLUSIVE MODE", blocker, transaction))
            await command.ExecuteNonQueryAsync();
        async Task<bool> Attempt(Func<Task> change)
        {
            try { await change(); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var changes = new[] { Attempt(() => workers.DisableAsync(fixture.A)), Attempt(() => workers.EnableAsync(fixture.B)) };
        try
        {
            var watch = Stopwatch.StartNew();
            int waiting = 0;
            while (watch.Elapsed < WaitLimit)
            {
                await using (var refresh = new NpgsqlCommand("SELECT pg_stat_clear_snapshot()", blocker, transaction))
                    await refresh.ExecuteNonQueryAsync();
                await using var command = new NpgsqlCommand("SELECT count(*)::integer FROM pg_stat_activity WHERE application_name = @application AND wait_event_type = 'Lock'", blocker, transaction);
                command.Parameters.AddWithValue("application", application);
                waiting = (int)(await command.ExecuteScalarAsync())!;
                if (waiting == 2) break;
                await Task.Delay(PollInterval);
            }
            Assert.Equal(2, waiting);
        }
        finally
        {
            await transaction.RollbackAsync();
            await Task.WhenAll(changes).WaitAsync(WaitLimit);
        }
        var results = await Task.WhenAll(changes);
        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);
        Assert.True(await states.IsEnabledAsync(fixture.A) || !await states.IsEnabledAsync(fixture.B));
    }

    private sealed class Fixture(string connectionString) : IDbContextFactory<PlatformDbContext>, IAsyncDisposable
    {
        private readonly string _prefix = "module-state-" + Guid.NewGuid().ToString("N");
        public string ConnectionString { get; } = connectionString;
        public List<string> OwnedIds { get; } = [];
        public string A => _prefix + "-A";
        public string B => _prefix + "-B";
        public string C => _prefix + "-C";
        public string Retired => _prefix + "-retired";
        public EfModuleStateStore Store => new EfModuleStateStore(this);

        public static async Task<Fixture> CreateAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Platform");
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Testing"
                || string.IsNullOrWhiteSpace(connectionString)
                || new NpgsqlConnectionStringBuilder(connectionString).Database != PlatformStorage.ValidationDatabase)
                throw new InvalidOperationException("Module state tests require the isolated Testing Platform connection.");
            var fixture = new Fixture(connectionString);
            fixture.OwnedIds.AddRange([fixture.A, fixture.B, fixture.C, fixture.Retired]);
            await using var context = fixture.CreateDbContext();
            await context.Database.MigrateAsync();
            return fixture;
        }

        public PlatformDbContext CreateDbContext() => CreateDbContext(ConnectionString);
        public static PlatformDbContext CreateDbContext(string connection)
        {
            var options = new DbContextOptionsBuilder<PlatformDbContext>();
            PlatformStorage.Configure(options, connection);
            return new PlatformDbContext(options.Options);
        }

        public ModuleStateService Service(IReadOnlyList<IAlxarafeModule> modules, string? connection = null)
        {
            ModuleGraph.Order(modules);
            IModuleStateStore store = connection is null ? Store : new EfModuleStateStore(new Fixture(connection));
            return new ModuleStateService(new ModuleRegistry([new Source(modules)]), store);
        }

        public static FakeModule Module(string id, params string[] dependencies) => new(new ModuleDescriptor(id, ModuleApi.CurrentVersion, dependencies));
        public IReadOnlyList<IAlxarafeModule> Chain(List<string>? calls = null) =>
        [
            new FakeModule(new ModuleDescriptor(C, ModuleApi.CurrentVersion, [B]), calls),
            new FakeModule(new ModuleDescriptor(B, ModuleApi.CurrentVersion, [A]), calls),
            new FakeModule(new ModuleDescriptor(A, ModuleApi.CurrentVersion, []), calls)
        ];

        public async ValueTask DisposeAsync()
        {
            await using var context = CreateDbContext();
            await context.ModuleStates.Where(state => OwnedIds.Contains(state.ModuleId)).ExecuteDeleteAsync();
        }
    }

    private sealed record Source(IEnumerable<IAlxarafeModule> Modules) : IModuleSource
    {
        public IEnumerable<IAlxarafeModule> GetModules() => Modules;
    }

    private sealed class FakeModule(ModuleDescriptor descriptor, List<string>? calls = null) : IAlxarafeModule
    {
        public ModuleDescriptor Descriptor { get; } = descriptor;
        public void ConfigureServices(IServiceCollection services) { }
        public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            calls?.Add(Descriptor.Id);
            return Task.CompletedTask;
        }
    }
}
