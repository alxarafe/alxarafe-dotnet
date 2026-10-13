using Alxarafe.AspNetCore;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Alxarafe.Host;

public sealed partial class PlatformExceptionHandler(
    IProblemDetailsService problemDetails,
    IStringLocalizer<CoreMessages> localizer,
    ILogger<PlatformExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code) = exception switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, CoreMessages.ValidationCode),
            _ => (0, string.Empty)
        };

        if (status == 0)
        {
            return false;
        }

        RejectedRequest(logger, exception, status, code);
        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = localizer[CoreMessages.ValidationTitle].Value,
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

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Request rejected with {StatusCode} and {ProblemCode}")]
    private static partial void RejectedRequest(ILogger logger, Exception exception, int statusCode, string problemCode);
}
