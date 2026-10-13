using Alxarafe.AspNetCore;
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
            const string code = AiAgentMessages.NotFoundCode;
            return entry is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: localizer[code].Value,
                    type: CoreMessages.ProblemTypePrefix + code, extensions: new Dictionary<string, object?> { [CoreMessages.CodeProperty] = code })
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

public sealed class AiAgentMessages
{
    public const string NotFoundCode = "ai.knowledge_not_found";
    public const string Required = "ai.validation.required";
    public const string InvalidUnicode = "ai.validation.invalid_unicode";
    public const string NullScalar = "ai.validation.null_scalar";
    public const string TooLong = "ai.validation.too_long";
}
public sealed record CreateKnowledgeEntryRequest(string Question, string Answer);
