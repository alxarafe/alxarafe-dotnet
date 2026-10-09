using System.Data.Common;
using System.Text;
using Alxarafe.Modules.AiAgent.Domain;
using Alxarafe.Modules.AiAgent.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Alxarafe.Modules.AiAgent.IntegrationTests;

public sealed class KnowledgePersistenceTests
{
    private static readonly string[] _migrations = ["202610060001_InitialKnowledge", "20261008000100_KnowledgeTextLimits"];

    [Fact]
    public async Task FreshDatabaseAppliesBothMigrationsAndScalarLengthConstraints()
    {
        await using var database = await TemporaryKnowledgeDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync();
        Assert.Equal(_migrations, await db.Database.GetAppliedMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT conname FROM pg_constraint WHERE conrelid = 'public.ai_knowledge'::regclass AND contype = 'c' ORDER BY conname", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        Assert.Equal(["CK_ai_knowledge_Answer_Length", "CK_ai_knowledge_Question_Length"], names);
        var id = Guid.NewGuid();
        var question = string.Concat(Enumerable.Repeat("\U0001F600", 1000));
        var answer = string.Concat(Enumerable.Repeat("\U00010400", 20000));
        await InsertAsync(database.ConnectionString, id, question, answer);
        var row = await db.Knowledge.AsNoTracking().SingleAsync(entry => entry.Id == id);
        Assert.Equal(question, row.Question);
        Assert.Equal(answer, row.Answer);
    }

    [Fact]
    public async Task IncrementalMigrationPreservesEarlierKnowledge()
    {
        await using var database = await TemporaryKnowledgeDatabase.CreateAsync();
        await using var db = database.CreateContext();
        // Preserve the legacy ID; EF resolves its shortened timestamp through the generated name.
        await db.GetService<IMigrator>().MigrateAsync(db.GetService<IMigrationsIdGenerator>().GetName(_migrations[0]));
        Assert.Equal([_migrations[0]], await db.Database.GetAppliedMigrationsAsync());
        var id = Guid.NewGuid();
        await InsertAsync(database.ConnectionString, id, "Earlier question", "Earlier answer");
        await db.Database.MigrateAsync();
        Assert.Equal(_migrations, await db.Database.GetAppliedMigrationsAsync());
        var row = await db.Knowledge.AsNoTracking().SingleAsync(entry => entry.Id == id);
        Assert.Equal("Earlier question", row.Question);
        Assert.Equal("Earlier answer", row.Answer);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(database.ConnectionString,
            Guid.NewGuid(), new string('a', 1001), "Answer"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("CK_ai_knowledge_Question_Length", exception.ConstraintName);
        Assert.Equal(1, await db.Knowledge.CountAsync());
    }

    [Theory]
    [InlineData("Question", 1001, "a")]
    [InlineData("Answer", 20001, "a")]
    [InlineData("Question", 1001, "\U0001F600")]
    [InlineData("Answer", 20001, "\U0001F600")]
    [InlineData("Question", 0, "a")]
    [InlineData("Answer", 0, "a")]
    public async Task DirectSqlRejectsTextOutsideBoundsWithoutLosingValidData(string field, int count, string scalar)
    {
        await using var database = await TemporaryKnowledgeDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync();
        var id = Guid.NewGuid();
        await InsertAsync(database.ConnectionString, id, "Valid question", "Valid answer");
        var invalid = string.Concat(Enumerable.Repeat(scalar, count));
        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(database.ConnectionString,
            Guid.NewGuid(), field == "Question" ? invalid : "Question", field == "Answer" ? invalid : "Answer"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal($"CK_ai_knowledge_{field}_Length", exception.ConstraintName);
        var row = await db.Knowledge.AsNoTracking().SingleAsync();
        Assert.Equal(id, row.Id);
        Assert.Equal("Valid question", row.Question);
        Assert.Equal("Valid answer", row.Answer);
    }

    [Fact]
    public async Task IncrementalMigrationRejectsInvalidLegacyDataWithoutChangingIt()
    {
        await using var database = await TemporaryKnowledgeDatabase.CreateAsync();
        var observer = new QuestionConstraintObserver();
        await using var db = new AiAgentDbContext(new DbContextOptionsBuilder<AiAgentDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(observer).Options);
        await db.GetService<IMigrator>().MigrateAsync(db.GetService<IMigrationsIdGenerator>().GetName(_migrations[0]));
        var id = Guid.NewGuid();
        const string question = "Valid earlier question";
        var answer = new string('a', 20001);
        await InsertAsync(database.ConnectionString, id, question, answer);
        var historyBefore = await ReadHistoryAsync(database.ConnectionString);
        var dataBefore = await db.Knowledge.AsNoTracking().SingleAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("CK_ai_knowledge_Answer_Length", exception.ConstraintName);
        // The first CHECK was actually visible inside this migration's transaction.
        Assert.True(observer.QuestionCheckObservedInsideTransaction);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var constraints = new NpgsqlCommand(
            "SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.ai_knowledge'::regclass AND contype = 'c'", connection);
        Assert.Equal(0L, await constraints.ExecuteScalarAsync());
        Assert.Equal(historyBefore, await ReadHistoryAsync(database.ConnectionString));
        Assert.Equal([_migrations[0]], await db.Database.GetAppliedMigrationsAsync());
        var row = await db.Knowledge.AsNoTracking().SingleAsync();
        Assert.Equal((id, question, answer), (row.Id, row.Question, row.Answer));
        Assert.Equal((dataBefore.Id, dataBefore.Question, dataBefore.Answer), (row.Id, row.Question, row.Answer));
    }

    [Fact]
    public async Task CombiningSequenceHasTwoScalarsAndRoundTripsWithoutNormalization()
    {
        const string decomposed = "e\u0301";
        Assert.Equal(2, decomposed.EnumerateRunes().Count());
        var entry = KnowledgeEntry.Create(decomposed, decomposed);
        // Boundary behavior proves that domain length counts both scalars.
        var boundary = string.Concat(Enumerable.Repeat(decomposed, 500));
        Assert.Equal(boundary, KnowledgeEntry.Create(boundary, "Answer").Question);
        Assert.Throws<ArgumentException>(() => KnowledgeEntry.Create(boundary + "e", "Answer"));
        await using var database = await TemporaryKnowledgeDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync();
        var repository = new EfKnowledgeRepository(db);
        await repository.AddAsync(entry, CancellationToken.None);
        var restored = await repository.GetByIdAsync(entry.Id, CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Equal(Encoding.UTF8.GetBytes(decomposed), Encoding.UTF8.GetBytes(restored.Question));
        Assert.Equal(Encoding.UTF8.GetBytes(decomposed), Encoding.UTF8.GetBytes(restored.Answer));
        Assert.NotEqual("\u00E9", restored.Question);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT char_length(\"Question\"), char_length(\"Answer\"), encode(convert_to(\"Question\", 'UTF8'), 'hex'), encode(convert_to(\"Answer\", 'UTF8'), 'hex') FROM ai_knowledge WHERE \"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", entry.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal(2, reader.GetInt32(1));
        Assert.Equal("65cc81", reader.GetString(2));
        Assert.Equal("65cc81", reader.GetString(3));
        Assert.False(await reader.ReadAsync());
    }

    private static async Task<List<(string Id, string Version)>> ReadHistoryAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"MigrationId\", \"ProductVersion\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var history = new List<(string, string)>();
        while (await reader.ReadAsync()) history.Add((reader.GetString(0), reader.GetString(1)));
        return history;
    }

    private sealed class QuestionConstraintObserver : DbCommandInterceptor
    {
        public bool QuestionCheckObservedInsideTransaction { get; private set; }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("ADD CONSTRAINT \"CK_ai_knowledge_Question_Length\"", StringComparison.Ordinal))
            {
                Assert.NotNull(command.Transaction);
                await using var query = command.Connection!.CreateCommand();
                query.Transaction = command.Transaction;
                query.CommandText = "SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.ai_knowledge'::regclass AND conname = 'CK_ai_knowledge_Question_Length'";
                QuestionCheckObservedInsideTransaction = (long)(await query.ExecuteScalarAsync(cancellationToken))! == 1;
            }
            return result;
        }
    }

    private static async Task InsertAsync(string connectionString, Guid id, string question, string answer)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO ai_knowledge (\"Id\", \"Question\", \"Answer\") VALUES (@id, @question, @answer)", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("question", question);
        command.Parameters.AddWithValue("answer", answer);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TemporaryKnowledgeDatabase(string name, string adminConnectionString, string connectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<TemporaryKnowledgeDatabase> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("ConnectionStrings__AiAgent");
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Testing" ||
                string.IsNullOrWhiteSpace(configured) || new NpgsqlConnectionStringBuilder(configured).Database != "alxarafe_ai_test")
                throw new InvalidOperationException("Persistence tests require Testing and the isolated AiAgent connection.");

            var name = $"ai001b1_{Guid.NewGuid():N}";
            var builder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres", Timeout = 15, CommandTimeout = 30 };
            var admin = builder.ConnectionString;
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
            builder.Database = name;
            return new TemporaryKnowledgeDatabase(name, admin, builder.ConnectionString);
        }

        public AiAgentDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<AiAgentDbContext>().UseNpgsql(ConnectionString).Options);

        public async ValueTask DisposeAsync()
        {
            // Only this randomly named database, successfully created by this fixture.
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
