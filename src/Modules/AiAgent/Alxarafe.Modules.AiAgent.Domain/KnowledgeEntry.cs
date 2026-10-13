using System.Buffers;
using System.Text;

namespace Alxarafe.Modules.AiAgent.Domain;

public sealed class KnowledgeEntry
{
    public const int QuestionMaxLength = 1000;
    public const int AnswerMaxLength = 20000;

    public Guid Id { get; }
    public string Question { get; }
    public string Answer { get; }

    private KnowledgeEntry(Guid id, string question, string answer)
    {
        if (id == Guid.Empty) throw new ArgumentException("The identifier must not be empty.", nameof(id));
        Id = id;
        Question = NormalizeText(question, QuestionMaxLength, nameof(question));
        Answer = NormalizeText(answer, AnswerMaxLength, nameof(answer));
    }

    private static string NormalizeText(string? value, int maximumLength, string parameter)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            throw new KnowledgeValidationException(parameter, KnowledgeValidationRule.Required, maximumLength);

        // Count Unicode scalar values, not UTF-16 code units or grapheme clusters.
        var remaining = normalized.AsSpan();
        var length = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done)
                throw new KnowledgeValidationException(parameter, KnowledgeValidationRule.InvalidUnicode, maximumLength);
            if (rune.Value == 0)
                throw new KnowledgeValidationException(parameter, KnowledgeValidationRule.NullScalar, maximumLength);
            if (++length > maximumLength)
                throw new KnowledgeValidationException(parameter, KnowledgeValidationRule.TooLong, maximumLength);
            remaining = remaining[consumed..];
        }

        return normalized;
    }

    public static KnowledgeEntry Create(string question, string answer) => new(Guid.NewGuid(), question, answer);
    public static KnowledgeEntry Rehydrate(Guid id, string question, string answer) => new(id, question, answer);
}
