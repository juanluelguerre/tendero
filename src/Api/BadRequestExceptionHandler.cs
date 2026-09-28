using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// A request the framework could not bind — <c>?page=abc</c>, <c>?sort=1</c> —
/// answered as what it is: the caller's mistake, a 400 with problem details.
///
/// In Development the framework THROWS on a bad binding instead of answering
/// (<c>ThrowOnBadRequest</c>, so the reason reaches a log), and with an
/// exception handler in the pipeline that throw became a 500 — a server error
/// for a typo in a URL, found when <c>?sort=</c> became a closed set. The
/// status is the exception's own, so a 415 stays a 415.
/// </summary>
internal sealed class BadRequestExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
            return false;

        httpContext.Response.StatusCode = badRequest.StatusCode;

        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = badRequest.StatusCode, Title = "The request is not valid.", Detail = badRequest.Message },
        });
    }
}
