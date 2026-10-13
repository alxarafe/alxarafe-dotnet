namespace Alxarafe.Modules.AiAgent.Domain;

public enum KnowledgeValidationRule { Required, InvalidUnicode, NullScalar, TooLong }

/// <summary>Structured invariant failure; contains no request culture or translated text.</summary>
public sealed class KnowledgeValidationException(string field, KnowledgeValidationRule rule, int maximumLength)
    : ArgumentException(rule switch
    {
        KnowledgeValidationRule.Required => "Knowledge text must not be empty after trimming.",
        KnowledgeValidationRule.InvalidUnicode => "Knowledge text must contain valid Unicode scalar values.",
        KnowledgeValidationRule.NullScalar => "Knowledge text must not contain U+0000.",
        KnowledgeValidationRule.TooLong => $"Knowledge text must not exceed {maximumLength} Unicode scalar values.",
        _ => "Unknown knowledge validation rule."
    }, field)
{
    public string Field { get; } = field;
    public KnowledgeValidationRule Rule { get; } = rule;
    public int MaximumLength { get; } = maximumLength;
}
