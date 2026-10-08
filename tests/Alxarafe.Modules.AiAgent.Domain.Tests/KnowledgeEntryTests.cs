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
}
