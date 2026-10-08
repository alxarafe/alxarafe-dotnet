using Alxarafe.Modules.AiAgent.Domain;

namespace Alxarafe.Modules.AiAgent.Application;

// This port keeps use cases independent of persistence and transport adapters.
public interface IKnowledgeRepository
{
    Task AddAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default);
    Task<KnowledgeEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record CreateKnowledgeEntryCommand(string Question, string Answer);
public sealed record KnowledgeEntryDto(Guid Id, string Question, string Answer);

public sealed class CreateKnowledgeEntryHandler(IKnowledgeRepository repository)
{
    public async Task<KnowledgeEntryDto> HandleAsync(CreateKnowledgeEntryCommand command, CancellationToken cancellationToken = default)
    {
        var entry = KnowledgeEntry.Create(command.Question, command.Answer);
        await repository.AddAsync(entry, cancellationToken);
        return new KnowledgeEntryDto(entry.Id, entry.Question, entry.Answer);
    }
}

public sealed class GetKnowledgeEntryByIdHandler(IKnowledgeRepository repository)
{
    public async Task<KnowledgeEntryDto?> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await repository.GetByIdAsync(id, cancellationToken);
        return entry is null ? null : new KnowledgeEntryDto(entry.Id, entry.Question, entry.Answer);
    }
}
