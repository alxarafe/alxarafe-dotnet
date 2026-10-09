using Alxarafe.Modules.AiAgent.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace Alxarafe.Modules.AiAgent.Http;

public static class AiAgentEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/ai/knowledge").WithTags("AiAgent");
        group.MapPost("", async (CreateKnowledgeEntryRequest request, CreateKnowledgeEntryHandler handler, CancellationToken ct) =>
        {
            var entry = await handler.HandleAsync(new CreateKnowledgeEntryCommand(request.Question, request.Answer), ct);
            return Results.Created($"/api/ai/knowledge/{entry.Id}", entry);
        }).RequireAuthorization(AiAgentPermissions.KnowledgeWrite)
            .Produces<KnowledgeEntryDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group.MapGet("/{id:guid}", async (Guid id, GetKnowledgeEntryByIdHandler handler, IStringLocalizer<AiAgentMessages> localizer, CancellationToken ct) =>
        {
            var entry = await handler.HandleAsync(id, ct);
            const string code = "ai.knowledge_not_found";
            return entry is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: localizer[code].Value,
                    type: $"https://alxarafe.dev/problems/{code}", extensions: new Dictionary<string, object?> { ["code"] = code })
                : Results.Ok(entry);
        }).RequireAuthorization(AiAgentPermissions.KnowledgeRead)
            .Produces<KnowledgeEntryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}

public static class AiAgentPermissions
{
    public const string KnowledgeRead = "ai.knowledge.read";
    public const string KnowledgeWrite = "ai.knowledge.write";
}

public sealed class AiAgentMessages { }
public sealed record CreateKnowledgeEntryRequest(string Question, string Answer);
