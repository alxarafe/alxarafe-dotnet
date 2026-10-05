using Alxarafe.Modules.Catalog.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Alxarafe.Host;

public sealed partial class PlatformExceptionHandler(
    IProblemDetailsService problemDetails,
    IStringLocalizer<PlatformMessages> localizer,
    ILogger<PlatformExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code) = exception switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, "validation_error"),
            ItemAlreadyExistsException => (StatusCodes.Status409Conflict, "catalog.sku_already_exists"),
            _ => (0, string.Empty)
        };

        if (status == 0)
        {
            UnhandledException(logger, exception, httpContext.Request.Path.ToString());
            return false;
        }

        RejectedRequest(logger, exception, status, code);
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

    [LoggerMessage(EventId = 1000, Level = LogLevel.Error, Message = "Unhandled exception while processing {Path}")]
    private static partial void UnhandledException(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Request rejected with {StatusCode} and {ProblemCode}")]
    private static partial void RejectedRequest(ILogger logger, Exception exception, int statusCode, string problemCode);
}
