using System.Text.Json;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
///
/// And one of the shop's own — a <see cref="ToolRefused"/> — is answered here,
/// as the result it is: marked an error, its text the problem and nothing
/// around it (the SDK would write *An error occurred invoking…* before it), and
/// counted with its code.
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
            catch (ToolRefused refused)
            {
                AgentToolCalls.Record(Surfaces.Mcp, tool, AgentToolCalls.Refused, refused.Code);
                return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = refused.Message }] };
            }
            catch (McpException)
            {
                AgentToolCalls.Record(Surfaces.Mcp, tool, AgentToolCalls.Refused);
                throw;
            }
            catch (Exception exception) when (NotTheToolsArguments(exception))
            {
                // The binder's, before the tool ran: the request is wrong, and
                // the agent is told how — a refusal, not a tool that broke.
                var refused = Refusals.NotTheToolsArguments(exception, CultureOf(request));
                AgentToolCalls.Record(Surfaces.Mcp, tool, AgentToolCalls.Refused, refused.Code);
                return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = refused.Message }] };
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AgentToolCalls.Record(Surfaces.Mcp, tool, AgentToolCalls.Failed);
                throw;
            }
        };

    /// <summary>
    /// Whether what was thrown is the SDK refusing to bind the call's arguments:
    /// one missing — it names its own parameter, `arguments` — or one that is
    /// not the JSON its type takes. A tool's own <see cref="ArgumentException"/>
    /// names something else and stays a failure of that tool.
    /// </summary>
    private static bool NotTheToolsArguments(Exception thrown) =>
        thrown is ArgumentException { ParamName: "arguments" } or JsonException;

    /// <summary>The language the call asked in, as the tools read it: the request's own header, or the shop's default.</summary>
    private static string CultureOf(RequestContext<CallToolRequestParams> request) =>
        CultureNegotiation.Resolve(null, request.Services?.GetService<IHttpContextAccessor>()?.HttpContext?.Request.Headers.AcceptLanguage);
}
