using Alxarafe.AspNetCore;
using Alxarafe.Modules.Catalog.Application;
using Alxarafe.Modules.Catalog.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Alxarafe.Modules.Catalog.Http;

public sealed partial class CatalogExceptionHandler(
    IProblemDetailsService problemDetails,
    IStringLocalizer<CatalogMessages> localizer,
    ILogger<CatalogExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title) = exception switch
        {
            ItemAlreadyExistsException => (StatusCodes.Status409Conflict, CatalogMessages.DuplicateCode,
                localizer[CatalogMessages.DuplicateCode].Value),
            ItemValidationException invalid => (StatusCodes.Status400BadRequest, CoreMessages.ValidationCode,
                localizer[CatalogMessages.InvalidField, invalid.Field, invalid.MaximumLength].Value),
            _ => (0, string.Empty, string.Empty)
        };
        if (status == 0) return false;
        // Do not log the exception message: it contains user-supplied domain data.
        RejectedRequest(logger, status, code);
        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = CoreMessages.ProblemTypePrefix + code
        };
        problem.Extensions[CoreMessages.CodeProperty] = code;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
        return true;
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Warning, Message = "Request rejected with {StatusCode} and {ProblemCode}")]
    private static partial void RejectedRequest(ILogger logger, int statusCode, string problemCode);
}
