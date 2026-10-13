using Alxarafe.AspNetCore;
using Alxarafe.Modules.AiAgent.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Alxarafe.Modules.AiAgent.Http;

public sealed class KnowledgeExceptionHandler(
    IProblemDetailsService problems,
    IStringLocalizer<AiAgentMessages> localizer) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not KnowledgeValidationException invalid) return false;
        var key = invalid.Rule switch
        {
            KnowledgeValidationRule.Required => AiAgentMessages.Required,
            KnowledgeValidationRule.InvalidUnicode => AiAgentMessages.InvalidUnicode,
            KnowledgeValidationRule.NullScalar => AiAgentMessages.NullScalar,
            KnowledgeValidationRule.TooLong => AiAgentMessages.TooLong,
            _ => throw new InvalidOperationException("Unknown knowledge validation rule.")
        };
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = localizer[key, invalid.Field, invalid.MaximumLength].Value,
            Type = CoreMessages.ProblemTypePrefix + CoreMessages.ValidationCode
        };
        problem.Extensions[CoreMessages.CodeProperty] = CoreMessages.ValidationCode;
        httpContext.Response.StatusCode = problem.Status.Value;
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
        return true;
    }
}
