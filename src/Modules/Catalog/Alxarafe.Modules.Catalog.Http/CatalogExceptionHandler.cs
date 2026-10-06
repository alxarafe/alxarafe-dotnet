using Alxarafe.Modules.Catalog.Application;
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
        if (exception is not ItemAlreadyExistsException) return false;

        const string code = "catalog.sku_already_exists";
        const int status = StatusCodes.Status409Conflict;
        // Do not log the exception message: it contains user-supplied domain data.
        RejectedRequest(logger, status, code);
        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = localizer[code].Value,
            Type = $"https://alxarafe.dev/problems/{code}"
        };
        problem.Extensions["code"] = code;
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
