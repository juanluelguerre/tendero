using System.Text.Json;

namespace ElGuerre.Tendero.Ucp.Catalog;

/// <summary>
/// The <c>meta</c> an agent may send with a UCP call over MCP, and what a shop
/// may conclude from it.
///
/// **It is optional, and the first version of this binding refused without it.**
/// The prose said *"`meta` (object, required) — must include
/// `ucp-agent.profile`"*, and it is wrong twice: `transports/mcp_tool_call.json`
/// makes `meta` an optional property of `arguments`, and `ucp-agent` is a nested
/// OBJECT whose `profile` is required — not a flat key spelled
/// <c>"ucp-agent.profile"</c>.
///
/// The difference is not cosmetic. A conformant agent sending a valid request
/// would have been refused by us, which is a worse failure than accepting a
/// sloppy one: being stricter than the protocol turns every correct caller into
/// an error.
///
/// So the rule is the schema's: absent is fine, present must be well formed.
/// </summary>
public static class UcpAgentMetadata
{
    /// <summary>The reserved member, nested rather than dotted.</summary>
    public const string Agent = "ucp-agent";

    /// <summary>
    /// The platform profile URI the agent advertises, or null when it sent no
    /// metadata at all — which the specification permits.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// When <c>meta</c> carries a <c>ucp-agent</c> that is not an object, or one
    /// with no <c>profile</c>. Malformed is not the same as absent.
    /// </exception>
    public static string? ProfileOf(IReadOnlyDictionary<string, JsonElement>? meta)
    {
        if (meta is null || !meta.TryGetValue(Agent, out var agent))
            return null;

        if (agent.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"`meta.{Agent}` must be an object.");

        if (!agent.TryGetProperty("profile", out var profile)
            || profile.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(profile.GetString()))
        {
            throw new ArgumentException($"`meta.{Agent}` must carry a `profile`.");
        }

        return profile.GetString();
    }
}
