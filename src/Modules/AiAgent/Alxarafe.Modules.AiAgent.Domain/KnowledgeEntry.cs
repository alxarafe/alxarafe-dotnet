namespace Alxarafe.Modules.AiAgent.Domain;

public sealed class KnowledgeEntry
{
    public Guid Id { get; }
    public string Question { get; }
    public string Answer { get; }

    private KnowledgeEntry(Guid id, string question, string answer)
    {
        if (id == Guid.Empty) throw new ArgumentException("The identifier must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("The question must not be empty.", nameof(question));
        if (string.IsNullOrWhiteSpace(answer)) throw new ArgumentException("The answer must not be empty.", nameof(answer));
        Id = id;
        Question = question.Trim();
        Answer = answer.Trim();
    }

    public static KnowledgeEntry Create(string question, string answer) => new(Guid.NewGuid(), question, answer);
    public static KnowledgeEntry Rehydrate(Guid id, string question, string answer) => new(id, question, answer);
}
