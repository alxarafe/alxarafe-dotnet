using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alxarafe.AspNetCore;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Security;
using Alxarafe.Security.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Alxarafe.Modules.Ai.Http;

public static class AiAssistantEndpoints
{
    public const string Path = "/api/ai/assistant/invocations";
    private const string MessageField = "message";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).RequireAuthorization(AiContract.AssistantPermission)
            .WithSharedContractErrors().WithTags("AI");
        group.MapPost("", InvokeAsync).Accepts<AiAssistantInvocationRequest>(MediaTypeNames.Application.Json)
            .Produces<AiAssistantInvocationResponse>(StatusCodes.Status200OK)
            .Produces<AiAssistantError>(StatusCodes.Status400BadRequest)
            .Produces<AiAssistantError>(StatusCodes.Status401Unauthorized)
            .Produces<AiAssistantError>(StatusCodes.Status403Forbidden)
            .Produces<AiAssistantError>(StatusCodes.Status500InternalServerError)
            .Produces<AiAssistantError>(StatusCodes.Status503ServiceUnavailable)
            .WithMetadata(new AiInvocationMetadata());
    }

    private static async Task<IResult> InvokeAsync(HttpContext context, AiAssistant assistant)
    {
        string? message = await ReadMessageAsync(context);
        if (message is null) return Failure(context, StatusCodes.Status400BadRequest, AiContract.InvalidRequest);
        string invocationId = context.Items[AiInvocationAuditMiddleware.InvocationIdItemKey] as string
            ?? Guid.NewGuid().ToString("N");
        try
        {
            var result = await assistant.InvokeAsync(message, context.User, Subject(context),
                context.TraceIdentifier, aiInvocationId: invocationId, cancellationToken: context.RequestAborted);
            AiToolUsage[] tools = result.ToolsUsed.Select(usage => new AiToolUsage(usage.CapabilityId, usage.InvocationId, usage.PrimitiveId)).ToArray();
            context.Items[AiInvocationAuditMiddleware.AuditContextItemKey] = new AiInvocationAuditContext(tools, null);
            string answer = result.Limitation == AiAnswerLimitation.None ? result.Answer
                : context.RequestServices.GetRequiredService<IStringLocalizer<AiMessages>>()[result.Limitation == AiAnswerLimitation.Access ? AiMessages.Access : AiMessages.Information].Value;
            return Results.Json(new AiAssistantInvocationResponse(invocationId, answer, tools));
        }
        catch (AiModelUnavailableException failure)
        {
            context.Items[AiInvocationAuditMiddleware.AuditContextItemKey] = new AiInvocationAuditContext(
                failure.ToolsUsed.Select(usage => new AiToolUsage(usage.CapabilityId, usage.InvocationId, usage.PrimitiveId)).ToArray(), AiContract.AssistantUnavailable);
            return Failure(context, StatusCodes.Status503ServiceUnavailable, AiContract.AssistantUnavailable);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (AiAssistantFailureException failure)
        {
            context.Items[AiInvocationAuditMiddleware.AuditContextItemKey] = new AiInvocationAuditContext(
                failure.ToolsUsed.Select(usage => new AiToolUsage(usage.CapabilityId, usage.InvocationId, usage.PrimitiveId)).ToArray(),
                AiContract.AssistantExecutionFailed);
            return Failure(context, StatusCodes.Status500InternalServerError, AiContract.AssistantExecutionFailed);
        }
        catch (AiModelFailureException) { return Failure(context, StatusCodes.Status500InternalServerError, AiContract.AssistantExecutionFailed); }
        catch (OperationCanceledException) { return Failure(context, StatusCodes.Status503ServiceUnavailable, AiContract.AssistantUnavailable); }
        catch (Exception) { return Failure(context, StatusCodes.Status500InternalServerError, AiContract.AssistantExecutionFailed); }
    }

    private static IResult Failure(HttpContext context, int status, string code)
    {
        if (status is not (StatusCodes.Status500InternalServerError or StatusCodes.Status503ServiceUnavailable)
            || context.Items[AiInvocationAuditMiddleware.AuditContextItemKey] is null)
            context.Items[AiInvocationAuditMiddleware.AuditContextItemKey] = new AiInvocationAuditContext([], code);
        return Results.Json(new AiAssistantError(code), statusCode: status);
    }

    private static async Task<string?> ReadMessageAsync(HttpContext context)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var properties = root.EnumerateObject().ToArray();
            if (properties.Length != 1 || properties[0].Name != MessageField
                || properties[0].Value.ValueKind != JsonValueKind.String) return null;
            string? message = properties[0].Value.GetString();
            return Valid(message) ? message : null;
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (BadHttpRequestException) { return null; }
    }

    private static bool Valid(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        bool nonWhitespace = false;
        int scalars = 0;
        for (int index = 0; index < message.Length; index++)
        {
            char character = message[index];
            if (character == '\0') return false;
            if (!char.IsWhiteSpace(character)) nonWhitespace = true;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= message.Length || !char.IsLowSurrogate(message[index + 1])) return false;
                index++;
            }
            else if (char.IsLowSurrogate(character)) return false;
            if (++scalars > AiContract.MessageMaximum) return false;
        }
        return nonWhitespace;
    }

    private static string? Subject(HttpContext context) =>
        context.Items[typeof(CoreUserState)] is CoreUserState state ? state.Id.ToString() : null;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiAssistantInvocationRequest([property: Required] string Message);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiAssistantInvocationResponse([property: Required] string InvocationId,
    [property: Required] string Answer, [property: Required] IReadOnlyList<AiToolUsage> ToolsUsed);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiToolUsage([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CapabilityId,
    [property: Required] string InvocationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PrimitiveId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiAssistantError(string Code) : ContractError(Code);
