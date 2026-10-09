using Alxarafe.Modules.AiAgent.Application;
using Alxarafe.Modules.AiAgent.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.AiAgent.Infrastructure;

public sealed class AiAgentDbContext(DbContextOptions<AiAgentDbContext> options) : DbContext(options)
{
    public DbSet<KnowledgeEntryRow> Knowledge => Set<KnowledgeEntryRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KnowledgeEntryRow>(entity =>
        {
            entity.ToTable("ai_knowledge", table =>
            {
                table.HasCheckConstraint("CK_ai_knowledge_Question_Length",
                    $"char_length(\"Question\") BETWEEN 1 AND {KnowledgeEntry.QuestionMaxLength}");
                table.HasCheckConstraint("CK_ai_knowledge_Answer_Length",
                    $"char_length(\"Answer\") BETWEEN 1 AND {KnowledgeEntry.AnswerMaxLength}");
            });
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Id).ValueGeneratedNever();
            entity.Property(entry => entry.Question).IsRequired();
            entity.Property(entry => entry.Answer).IsRequired();
        });
    }
}

public sealed class KnowledgeEntryRow
{
    public Guid Id { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

public sealed class EfKnowledgeRepository(AiAgentDbContext db) : IKnowledgeRepository
{
    public async Task AddAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default)
    {
        db.Knowledge.Add(new KnowledgeEntryRow { Id = entry.Id, Question = entry.Question, Answer = entry.Answer });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<KnowledgeEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.Knowledge.AsNoTracking().SingleOrDefaultAsync(entry => entry.Id == id, cancellationToken);
        return row is null ? null : KnowledgeEntry.Rehydrate(row.Id, row.Question, row.Answer);
    }
}
