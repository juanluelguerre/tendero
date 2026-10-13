using System.Text.Json;
using ElGuerre.Tendero.Mcp.Tools;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
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

    /// <summary>The SDK's options, with the API's rule for enums first (ADR 0043).</summary>
    public static readonly JsonSerializerOptions ToolAnswers = AnswerOptions();

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
            // With the API's rule for enums given explicitly (ADR 0043): these
            // tools answer with the API's own records, and the SDK serialises
            // with its options, not ASP.NET's. Its defaults spell an enum the
            // same way today, which is a default of somebody else's package.
            .WithTools<TenderoTools>(ToolAnswers)
            // And the UCP catalogue binding, which is a different contract with
            // different shapes under names the specification fixes. Both on one
            // server because MapMcp offers one server per process.
            .WithTools<UcpCatalogTools>()
            // And checkout's binding (P11-1): the one set of tools on this server
            // that needs an agent's token, checked tool by tool (ADR 0039).
            .WithTools<UcpCheckoutTools>()
            // Every call counted once, by tool and outcome (the tracking plan).
            .WithRequestFilters(filters => filters
                .AddCallToolFilter(ToolCallCounting.Filter)
                // *Servers MUST include caching hints* on a tool list (MCP,
                // Caching). The SDK's are the safe ones — private, stale at
                // once — and this list is the same for whoever asks and changes
                // only with a deployment, which is what `public` is for.
                .AddListToolsFilter(next => async (request, cancellationToken) =>
                {
                    var tools = await next(request, cancellationToken);
                    tools.TimeToLive = ToolListFreshFor;
                    tools.CacheScope = CacheScope.Public;
                    return tools;
                }))
            .Services;

    /// <summary>How long a client may keep the tool list without asking again.</summary>
    public static readonly TimeSpan ToolListFreshFor = TimeSpan.FromMinutes(5);

    /// <summary>The rate limit's name: anybody may call, so what bounds a caller is how much.</summary>
    public const string PerClient = "Mcp.PerClient";

    public static IEndpointConventionBuilder MapTenderoMcp(this IEndpointRouteBuilder endpoints)
    {
        // What a 401 from here points at (RFC 9728).
        endpoints.MapMcpResourceMetadata();

        return endpoints
            .MapMcp(Path)
            // *Servers MUST validate the Origin header on all incoming connections*
            // (the specification, Streamable HTTP): a page on another site, open in
            // a shopper's browser, can post here, and CORS only stops it reading the
            // answer. The SDK does not do this for a server; found by asking, on
            // 2026-10-13, with an Origin nobody knows — which was served.
            .AddEndpointFilter(async (invocation, next) =>
                KnownOrigin(invocation.HttpContext) ? await next(invocation) : Results.StatusCode(StatusCodes.Status403Forbidden))
            // Who may call the tool this call names, said with HTTP's own statuses.
            .AddEndpointFilter(async (invocation, next) =>
                await McpAuthorization.RefusalAsync(invocation.HttpContext) ?? await next(invocation))
            // And *Servers MUST rate limit tool invocations*.
            .RequireRateLimiting(PerClient)
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

    /// <summary>
    /// Whether a request may be served by its `Origin`. One that names none is not
    /// a browser's — an agent, a command line — and the rule is about a header
    /// that is present. One that is present has to be the address this server
    /// answers at, or one of the shop's own front ends: the same list CORS is
    /// given, so there is one answer to *whose pages are ours*.
    /// </summary>
    private static bool KnownOrigin(HttpContext http)
    {
        if (!http.Request.Headers.TryGetValue("Origin", out var sent) || sent.Count == 0)
            return true;

        var origin = sent.ToString();
        if (string.Equals(origin, $"{http.Request.Scheme}://{http.Request.Host}", StringComparison.OrdinalIgnoreCase))
            return true;

        var ours = http.RequestServices.GetRequiredService<IConfiguration>().GetSection("Cors:Origins").Get<string[]>()
            ?? DefaultOrigins;
        return ours.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The two Angular apps as a developer runs them: what CORS falls back to as well.</summary>
    private static readonly string[] DefaultOrigins = ["http://localhost:4200", "http://localhost:4201"];

    private static JsonSerializerOptions AnswerOptions()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        options.Converters.Insert(0, WireJson.Enums());
        options.Converters.Insert(0, WireJson.Money());
        options.MakeReadOnly();
        return options;
    }
}
