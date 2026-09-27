using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace ElGuerre.Tendero.Ucp;

/// <summary>
/// `tendero.agent.tool_calls` for UCP's REST routes, catalogue and checkout alike:
/// one filter, so a route added later is counted the same way or not at all.
/// </summary>
public static class UcpToolCounting
{
    /// <summary>
    /// Counts the call into `tendero.agent.tool_calls` by what came back: a 2xx
    /// answered, a 4xx refused — a missing profile, a missing id, an unknown
    /// product — and anything else, thrown or 5xx, failed. A validation failure
    /// is thrown by the dispatcher and answered as a 400 further out, so it is
    /// counted as the refusal it becomes.
    /// </summary>
    public static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> CountedAs(string tool) =>
        async (context, next) =>
        {
            try
            {
                var result = await next(context);
                var status = ((result as INestedHttpResult)?.Result ?? result) is IStatusCodeHttpResult { StatusCode: { } code }
                    ? code
                    : StatusCodes.Status200OK;

                AgentToolCalls.Record(Surfaces.Ucp, tool, status switch
                {
                    < 400 => AgentToolCalls.Answered,
                    < 500 => AgentToolCalls.Refused,
                    _ => AgentToolCalls.Failed
                });

                return result;
            }
            catch (FluentValidation.ValidationException)
            {
                AgentToolCalls.Record(Surfaces.Ucp, tool, AgentToolCalls.Refused);
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AgentToolCalls.Record(Surfaces.Ucp, tool, AgentToolCalls.Failed);
                throw;
            }
        };
}
