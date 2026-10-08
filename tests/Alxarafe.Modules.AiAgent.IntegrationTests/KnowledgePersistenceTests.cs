using Alxarafe.Modules.AiAgent.Infrastructure;
using Microsoft.EntityFrameworkCore;
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
        await using var db = database.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(db.GetService<IMigrationsIdGenerator>().GetName(_migrations[0]));
        var id = Guid.NewGuid();
        var question = new string('a', 1001);
        await InsertAsync(database.ConnectionString, id, question, "Earlier answer");
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal([_migrations[0]], await db.Database.GetAppliedMigrationsAsync());
        var row = await db.Knowledge.AsNoTracking().SingleAsync();
        Assert.Equal(id, row.Id);
        Assert.Equal(question, row.Question);
        Assert.Equal("Earlier answer", row.Answer);
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

            var name = $"ai001b_{Guid.NewGuid():N}";
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
