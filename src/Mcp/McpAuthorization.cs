using System.Reflection;
using System.Text.Json;
using ElGuerre.Tendero.Mcp.Tools;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace ElGuerre.Tendero.Mcp;

/// <summary>
/// Who may call what on `/mcp`, said the way HTTP says it (MCP, Authorization;
/// RFC 9728; RFC 6750).
///
/// The endpoint is open and five of its tools are not (ADR 0039), so the door
/// cannot be the endpoint's: it is decided per call, by the tool the call
/// names. That used to happen inside the tool, where all that is left to answer
/// with is a JSON-RPC error over a 200 — which a client reads as a call that
/// failed, and a client that would have signed in never learns it could. Here
/// the same decision is made before the server is reached: no token is a 401
/// that names the resource's metadata, the metadata names the issuer, and a
/// person's token is a 403.
///
/// The tool is read from the body, not from the `Mcp-Name` header the
/// 2026-07-28 revision mirrors it into: a client of an earlier revision sends
/// no such header, and this server answers those too.
/// </summary>
internal static class McpAuthorization
{
    /// <summary>Where the resource says who issues its tokens: RFC 9728's well-known path, with the resource's own after it.</summary>
    public const string MetadataPath = "/.well-known/oauth-protected-resource" + TenderoMcpServer.Path;

    /// <summary>
    /// The tools that need an agent's token: every one checkout's binding
    /// declares, read from it rather than listed, so a sixth is behind the door
    /// the day it is written.
    /// </summary>
    private static readonly HashSet<string> ForAnAgent = typeof(UcpCheckoutTools)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
        .OfType<string>()
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>The answer when this call may not go on, or null when it may.</summary>
    public static async ValueTask<IResult?> RefusalAsync(HttpContext http)
    {
        var signedIn = http.User.Identity?.IsAuthenticated == true;

        // *Invalid or expired tokens MUST receive a HTTP 401*: on an open
        // endpoint a token nobody signed is otherwise ignored, and its bearer
        // served as nobody without being told.
        if (!signedIn && http.Request.Headers.ContainsKey("Authorization"))
            return Challenge(http, "error=\"invalid_token\"");

        if (await ToolCalledAsync(http.Request) is not { } tool || !ForAnAgent.Contains(tool))
            return null;

        if (!signedIn)
            return Challenge(http);

        var allowed = await http.RequestServices.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(http.User, TenderoPolicyNames.Agent);
        return allowed.Succeeded ? null : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// <c>GET /.well-known/oauth-protected-resource/mcp</c> — RFC 9728: what the
    /// challenge points at, so a client finds the issuer without being told.
    /// Public by nature, as discovery is; and out of the OpenAPI document with
    /// the endpoint it describes.
    /// </summary>
    public static IEndpointConventionBuilder MapMcpResourceMetadata(this IEndpointRouteBuilder endpoints) =>
        endpoints
            .MapGet(MetadataPath, (HttpContext http, IConfiguration configuration) =>
            {
                http.Response.Headers.CacheControl = "public, max-age=3600";

                return Results.Json(new Dictionary<string, object>
                {
                    ["resource"] = Origin(http) + TenderoMcpServer.Path,
                    ["authorization_servers"] = new[] { Issuer(http, configuration) },
                    ["bearer_methods_supported"] = new[] { "header" },
                    ["resource_name"] = "Tendero",
                });
            })
            .AllowAnonymous()
            .WithMetadata(new ExcludeFromDescriptionAttribute());

    private static IResult Challenge(HttpContext http, string? error = null)
    {
        var metadata = $"resource_metadata=\"{Origin(http)}{MetadataPath}\"";
        http.Response.Headers.WWWAuthenticate = error is null ? $"Bearer {metadata}" : $"Bearer {error}, {metadata}";

        return Results.StatusCode(StatusCodes.Status401Unauthorized);
    }

    private static string Origin(HttpContext http) => $"{http.Request.Scheme}://{http.Request.Host}";

    /// <summary>The issuer the API validates tokens against; one served by this host is named by its full address.</summary>
    private static string Issuer(HttpContext http, IConfiguration configuration)
    {
        var authority = configuration["Authentication:Authority"] ?? string.Empty;
        return Uri.TryCreate(authority, UriKind.Absolute, out _) ? authority : Origin(http) + authority;
    }

    /// <summary>
    /// The tool a `tools/call` names, or null for anything else. The body is
    /// buffered and left where it was found: the server reads it next.
    /// </summary>
    private static async ValueTask<string?> ToolCalledAsync(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
            return null;

        request.EnableBuffering();
        try
        {
            using var message = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
            var root = message.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("method", out var method) && method.ValueEquals("tools/call")
                && root.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                    ? name.GetString()
                    : null;
        }
        catch (JsonException)
        {
            // Not JSON is the server's to refuse, in the protocol's own words.
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }
}
