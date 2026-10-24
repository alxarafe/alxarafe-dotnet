using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Infrastructure;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class AiUsageTests
{
    private static readonly string[] ForbiddenDependencies = ["Alxarafe.Modules.Ai.Infrastructure", "Alxarafe.Modules.Ai.Persistence", "Microsoft.EntityFrameworkCore"];
    private static readonly string[] ForbiddenProperties = ["Content", "Answer", "Prompt", "Messages", "Headers", "ApiKey", "Authorization", "Reasoning"];
    [Fact]
    public void ExistingModelPortOwnsProviderNeutralUsageWithoutTransportOrPersistenceDependencies()
    {
        Assert.Equal(typeof(Task<AiModelResponse>), typeof(IAiModelClient).GetMethod(nameof(IAiModelClient.CompleteAsync))!.ReturnType);
        Assert.Equal(typeof(IAiModelClient).Assembly, typeof(AiUsageRecord).Assembly);
        foreach (var reference in typeof(IAiModelClient).Assembly.GetReferencedAssemblies())
            Assert.DoesNotContain(reference.Name!, ForbiddenDependencies);
        Assert.Contains(typeof(IAiModelClient), typeof(GroqAiModelClient).GetInterfaces());
        Assert.Contains(typeof(IAiModelClient), typeof(OllamaAiModelClient).GetInterfaces());
    }

    [Fact]
    public void UsageShapesCannotStoreConversationOrAuthenticationPayloads()
    {
        foreach (var type in new[] { typeof(AiUsageRecord), typeof(AiInvocationUsage) })
            foreach (var property in type.GetProperties())
                Assert.DoesNotContain(property.Name, ForbiddenProperties);
    }
}
