using ElGuerre.Tendero.Mcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace ElGuerre.Tendero.Mcp;

/// <summary>
/// The read-only MCP server, mounted inside the API process.
///
/// This is the second of Tendero's three agent surfaces, and what separates the
/// three is the trust model rather than the transport. WebMCP runs in the
/// shopper's own tab and inherits their session. This one is an EXTERNAL
/// process — Claude Desktop, an editor, somebody's script — and it is
/// anonymous by explicit decision: the tools read the public catalogue, which is
/// public on the web too, so asking an agent to hold a token to read what a
/// browser reads without one would be theatre. UCP in phase 11 is the opposite
/// case and carries both a principal and a signed mandate.
/// </summary>
public static class TenderoMcpServer
{
    /// <summary>
    /// The path an agent connects to. Fixed rather than configurable: it goes in
    /// a client's configuration file by hand, and a path that varies per
    /// deployment is a support question rather than a feature.
    /// </summary>
    public const string Path = "/mcp";

    /// <summary>
    /// Read from the assembly rather than written here, so it cannot say one
    /// thing while the binary says another.
    /// </summary>
    private static readonly string Version =
        typeof(TenderoMcpServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static IServiceCollection AddTenderoMcp(this IServiceCollection services) =>
        services
            // The tools read `Accept-Language` off the request the JSON-RPC call
            // arrived on, which is what lets ADR 0013's chain be the same chain
            // here as over HTTP rather than an analogue of it.
            .AddHttpContextAccessor()
            .AddMcpServer(options => options.ServerInfo = new Implementation
            {
                // What an agent shows a person when it lists what it is
                // connected to, so it is the shop's name and not the assembly's.
                Name = "tendero",
                Title = "Tendero",
                Version = Version,
            })
            // Streamable HTTP rather than stdio: the server lives in the API
            // process, and a process that already answers HTTP should not also
            // be launched as a child process to be talked to.
            .WithHttpTransport()
            // The tool set is declared in one type, so what the shop offers an
            // agent is one file to read and one file to change.
            .WithTools<TenderoTools>()
            // And the UCP catalogue binding, which is a different contract with
            // different shapes under names the specification fixes. Both on one
            // server because MapMcp offers one server per process.
            .WithTools<UcpCatalogTools>()
            .Services;

    public static IEndpointConventionBuilder MapTenderoMcp(this IEndpointRouteBuilder endpoints) =>
        endpoints
            .MapMcp(Path)
            // The explicit decision the endpoint-policy test insists on. Read
            // tools over a public catalogue; nothing here writes.
            .AllowAnonymous()
            // Out of the OpenAPI document on purpose. That document is the REST
            // API's shape — it generates the frontend's types and will back the
            // UCP capability schemas — and MCP is JSON-RPC over one route, whose
            // real contract is the tool list an agent gets at `initialize`.
            // Describing it as a POST that takes "anything" would make the
            // document less true, not more complete.
            .WithMetadata(new ExcludeFromDescriptionAttribute());
}
