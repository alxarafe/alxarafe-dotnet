using System.Text.Json;

namespace Alxarafe.Modules.Ai.Application;

public static class AiRoles
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";
}

public sealed record AiToolDefinition(string Name, string CapabilityId, string DisplayName, string Description,
    JsonElement InputSchema, string? PrimitiveId = null);

public sealed record AiToolRequest(string Id, string Name, JsonElement Arguments);

public sealed record AiModelMessage(string Role, string? Content, IReadOnlyList<AiToolRequest>? ToolCalls = null,
    string? ToolCallId = null, string? Name = null);

public sealed record AiModelRequest(string SystemPrompt, IReadOnlyList<AiModelMessage> Messages,
    IReadOnlyList<AiToolDefinition> Tools, string? InvocationId = null);

public sealed record AiModelResponse(string? Answer, IReadOnlyList<AiToolRequest> ToolCalls,
    IReadOnlyList<AiUsageRecord>? Executions = null);

public interface IAiModelClient
{
    Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken = default);
}

public sealed class AiModelUnavailableException(string message = "The assistant is temporarily unavailable.",
    IReadOnlyList<AiToolUsageResult>? toolsUsed = null, IReadOnlyList<AiUsageRecord>? executions = null) : Exception(message)
{
    public IReadOnlyList<AiToolUsageResult> ToolsUsed { get; } = toolsUsed ?? [];
    public IReadOnlyList<AiUsageRecord> Executions { get; } = executions ?? [];
}

public sealed class AiModelFailureException(string message = "The assistant failed.", IReadOnlyList<AiUsageRecord>? executions = null)
    : Exception(message)
{
    public IReadOnlyList<AiUsageRecord> Executions { get; } = executions ?? [];
}

public sealed class AiModelCancelledException(IReadOnlyList<AiUsageRecord> executions, CancellationToken cancellationToken)
    : OperationCanceledException("The model call was cancelled.", cancellationToken)
{
    public IReadOnlyList<AiUsageRecord> Executions { get; } = executions;
}

public sealed class AiAssistantFailureException(string message = "The assistant could not complete the invocation.",
    IReadOnlyList<AiToolUsageResult>? toolsUsed = null) : Exception(message)
{
    public IReadOnlyList<AiToolUsageResult> ToolsUsed { get; } = toolsUsed ?? [];
}
