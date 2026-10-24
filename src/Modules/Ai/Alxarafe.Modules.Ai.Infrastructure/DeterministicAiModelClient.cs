using System.Text.Json;
using Alxarafe.Modules.Ai.Application;

namespace Alxarafe.Modules.Ai.Infrastructure;

public sealed class DeterministicAiModelClient : IAiModelClient
{
    private const string DirectAnswer = "The assistant answered without using an ERBAS capability.";
    private const string ToolAnswer = "Capability results were collected for the answer.";
    private static readonly JsonElement EmptyArguments = JsonDocument.Parse("{}").RootElement.Clone();
    private static readonly HashSet<string> LeadingVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "list", "get", "read", "search", "fetch", "retrieve", "show", "find"
    };

    public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Messages.Any(message => message.Role == AiRoles.Tool))
            return Task.FromResult(new AiModelResponse(ToolAnswer, []));

        string userMessage = request.Messages.LastOrDefault(message => message.Role == AiRoles.User)?.Content ?? "";
        var selected = new List<(int Index, AiToolDefinition Tool)>();
        foreach (var tool in request.Tools)
        {
            int index = EarliestMatch(userMessage, tool);
            if (index >= 0) selected.Add((index, tool));
        }
        if (selected.Count == 0) return Task.FromResult(new AiModelResponse(DirectAnswer, []));

        AiToolRequest[] calls = selected.OrderBy(entry => entry.Index)
            .Select(entry => new AiToolRequest(Guid.NewGuid().ToString("N"), entry.Tool.Name, EmptyArguments))
            .ToArray();
        return Task.FromResult(new AiModelResponse(null, calls));
    }

    private static int EarliestMatch(string message, AiToolDefinition tool)
    {
        int best = -1;
        foreach (string phrase in new[] { tool.PrimitiveId ?? tool.CapabilityId, Subject(tool.DisplayName) })
        {
            if (string.IsNullOrWhiteSpace(phrase)) continue;
            int index = message.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (best < 0 || index < best)) best = index;
        }
        return best;
    }

    private static string Subject(string displayName)
    {
        int space = displayName.IndexOf(' ');
        return space > 0 && LeadingVerbs.Contains(displayName[..space]) ? displayName[(space + 1)..] : displayName;
    }
}
