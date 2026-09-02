using ElGuerre.Tendero.Search.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// Translates a search engine failure into a 503. Two things it does on purpose:
///
/// The status is 503 and not 500. The service is fine, its dependency is not:
/// that is retryable, and a client (or an agent) has to be able to tell.
///
/// The body does NOT carry the detail. The Elastic client returns an audit trail
/// with the full trace, the node and the internal port; it is useful for
/// diagnosis and that is why it travels on the exception, which ASP.NET logs
/// whole. What it must never do is go out in the response: it exposes topology
/// and says nothing useful to somebody looking for a coffee maker. The text
/// follows DESIGN.md's voice — what happened and what to do — not an apology.
/// </summary>
internal sealed class SearchUnavailableExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not SearchUnavailableException)
            return false;

        var problem = TypedResults.Problem(
            title: "Search is unavailable right now.",
            detail: "The catalogue is fine; the search engine is not answering. Try again in a moment.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

        await problem.ExecuteAsync(httpContext);

        return true;
    }
}
