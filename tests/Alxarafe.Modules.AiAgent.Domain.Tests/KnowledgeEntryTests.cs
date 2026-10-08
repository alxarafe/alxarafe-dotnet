using Alxarafe.Modules.AiAgent.Domain;
using Xunit;

namespace Alxarafe.Modules.AiAgent.Domain.Tests;

public sealed class KnowledgeEntryTests
{
    [Fact]
    public void CreateTrimsTextAndGeneratesDistinctIdentifiers()
    {
        var first = KnowledgeEntry.Create("  ¿Cómo funciona?  ", "  Primera línea.\nSegunda línea.  ");
        var second = KnowledgeEntry.Create("¿Cómo funciona?", "Otra respuesta.");
        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("¿Cómo funciona?", first.Question);
        Assert.Equal("Primera línea.\nSegunda línea.", first.Answer);
    }

    [Theory]
    [InlineData(null, "Answer", "question")]
    [InlineData("", "Answer", "question")]
    [InlineData("   ", "Answer", "question")]
    [InlineData("Question", null, "answer")]
    [InlineData("Question", "", "answer")]
    [InlineData("Question", "   ", "answer")]
    public void CreateRejectsMissingText(string? question, string? answer, string parameter)
    {
        var exception = Assert.Throws<ArgumentException>(() => KnowledgeEntry.Create(question!, answer!));
        Assert.Equal(parameter, exception.ParamName);
    }

    [Fact]
    public void RehydratePreservesStoredIdentifierAndText()
    {
        var id = Guid.NewGuid();
        var entry = KnowledgeEntry.Rehydrate(id, "Question", "Answer");
        Assert.Equal(id, entry.Id);
        Assert.Equal("Question", entry.Question);
        Assert.Equal("Answer", entry.Answer);
    }

    [Fact]
    public void RehydrateRejectsEmptyIdentifier()
    {
        Assert.Throws<ArgumentException>(() => KnowledgeEntry.Rehydrate(Guid.Empty, "Question", "Answer"));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("\U0001F600")]
    public void CreateAcceptsExactScalarLimitsAfterTrimming(string scalar)
    {
        var question = string.Concat(Enumerable.Repeat(scalar, 1000));
        var answer = string.Concat(Enumerable.Repeat(scalar, 20000));
        var entry = KnowledgeEntry.Create($"  {question}  ", $"\t{answer}\n");
        Assert.Equal(question, entry.Question);
        Assert.Equal(answer, entry.Answer);
    }

    [Theory]
    [InlineData(true, "a")]
    [InlineData(false, "a")]
    [InlineData(true, "\U0001F600")]
    [InlineData(false, "\U0001F600")]
    public void CreateRejectsOneScalarBeyondEachLimit(bool questionIsTooLong, string scalar)
    {
        var text = string.Concat(Enumerable.Repeat(scalar, questionIsTooLong ? 1001 : 20001));
        var exception = Assert.Throws<ArgumentException>(() => KnowledgeEntry.Create(
            questionIsTooLong ? text : "Question", questionIsTooLong ? "Answer" : text));
        Assert.Equal(questionIsTooLong ? "question" : "answer", exception.ParamName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RehydrateEnforcesTheSameLengthInvariants(bool questionIsTooLong)
    {
        var text = new string('a', questionIsTooLong ? 1001 : 20001);
        Assert.Throws<ArgumentException>(() => KnowledgeEntry.Rehydrate(Guid.NewGuid(),
            questionIsTooLong ? text : "Question", questionIsTooLong ? "Answer" : text));
    }

    [Fact]
    public void CreatePreservesInteriorWhitespaceAndCase()
    {
        var entry = KnowledgeEntry.Create("  A  Question\tHere  ", "\nAn  Answer\nHere\n");
        Assert.Equal("A  Question\tHere", entry.Question);
        Assert.Equal("An  Answer\nHere", entry.Answer);
    }

    [Fact]
    public void CreateTrimsUnicodeWhitespaceBeforeValidation()
    {
        var entry = KnowledgeEntry.Create("\u2003Question\u2003", "\u2003Answer\u2003");
        Assert.Equal("Question", entry.Question);
        Assert.Equal("Answer", entry.Answer);
        Assert.Throws<ArgumentException>(() => KnowledgeEntry.Create("\u2003", "Answer"));
    }

    [Fact]
    public void CombiningMarksCountSeparatelyWithoutUnicodeNormalization()
    {
        var question = string.Concat(Enumerable.Repeat("e\u0301", 500));
        Assert.Equal(question, KnowledgeEntry.Create(question, "Answer").Question);
        Assert.Throws<ArgumentException>(() => KnowledgeEntry.Create(question + "e", "Answer"));
    }

    [Fact]
    public void CreateRejectsUnpairedSurrogates()
    {
        var invalid = new string('\uD800', 1);
        Assert.Throws<ArgumentException>(() => KnowledgeEntry.Create(invalid, "Answer"));
        Assert.Throws<ArgumentException>(() => KnowledgeEntry.Create("Question", invalid));
    }

    [Theory]
    [InlineData("\u0000", "Answer", "question")]
    [InlineData("Question", "\u0000", "answer")]
    [InlineData("before\u0000after", "Answer", "question")]
    [InlineData("Question", "before\u0000after", "answer")]
    public void NullScalarIsRejectedRatherThanSilentlyRemoved(string question, string answer, string parameter)
    {
        var exception = Assert.Throws<ArgumentException>(() => KnowledgeEntry.Create(question, answer));
        Assert.Equal(parameter, exception.ParamName);
        Assert.Contains("U+0000", exception.Message);
        Assert.DoesNotContain("before", exception.Message);
        Assert.DoesNotContain("after", exception.Message);
        Assert.Throws<ArgumentException>(() => KnowledgeEntry.Rehydrate(Guid.NewGuid(), question, answer));
    }

    [Fact]
    public void RepresentableTextIsPreservedWithoutReplacementOrNormalization()
    {
        const string question = "before\U0001F600e\u0301after";
        const string answer = "before\t\U00010400after";
        var entry = KnowledgeEntry.Create(question, answer);
        Assert.Equal(question, entry.Question);
        Assert.Equal(answer, entry.Answer);
    }
}
