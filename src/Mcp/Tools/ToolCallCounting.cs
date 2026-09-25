using ElGuerre.Tendero.SharedKernel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ElGuerre.Tendero.Mcp.Tools;

/// <summary>
/// Counts every tool call into `tendero.agent.tool_calls`, once, around the
/// whole call — one filter rather than a line in each of eight tools, which is
/// the line the ninth tool would forget.
///
/// A refusal here is an <see cref="McpException"/>: every "no" the tools say
/// goes through one (<see cref="Refusals"/>), and it reaches the agent as an
/// error result carrying our sentence. Anything else thrown is a tool breaking.
/// </summary>
internal static class ToolCallCounting
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, cancellationToken) =>
        {
            var tool = request.Params?.Name ?? "unknown";

            try
            {
                var result = await next(request, cancellationToken);
                AgentToolCalls.Record(Surfaces.Mcp, tool, result.IsError == true ? AgentToolCalls.Refused : AgentToolCalls.Answered);
                return result;
            }
            catch (McpException)
            {
                AgentToolCalls.Record(Surfaces.Mcp, tool, AgentToolCalls.Refused);
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AgentToolCalls.Record(Surfaces.Mcp, tool, AgentToolCalls.Failed);
                throw;
            }
        };
}
