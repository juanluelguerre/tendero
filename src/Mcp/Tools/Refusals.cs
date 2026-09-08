using FluentValidation;
using ModelContextProtocol;

namespace ElGuerre.Tendero.Mcp.Tools;

/// <summary>
/// A rejected request, in the words of this transport.
///
/// The dispatcher's validation step throws the same <see cref="ValidationException"/>
/// whoever asked, and each transport says it in its own protocol: over HTTP the
/// API's <c>ValidationExceptionHandler</c> turns it into a ProblemDetails, and
/// here it becomes a JSON-RPC error carrying the same sentences. This is the one
/// thing in the MCP project that is not a straight dispatch, and it is the
/// transport's own job rather than logic: without it a one-character query
/// reaches the agent as an unhandled exception and a stack trace, which tells it
/// nothing about how to ask again.
/// </summary>
internal static class Refusals
{
    public static async Task<T> Readable<T>(Func<Task<T>> dispatch)
    {
        try
        {
            return await dispatch();
        }
        catch (ValidationException exception)
        {
            // Every failure, not the first: an agent that fixes one rule at a
            // time takes one round trip per mistake.
            throw new McpException(string.Join(" ", exception.Errors.Select(e => e.ErrorMessage)));
        }
    }
}
