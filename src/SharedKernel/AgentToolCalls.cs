using System.Diagnostics.Metrics;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// `tendero.agent.tool_calls` (the tracking plan): what external agents call
/// over MCP and UCP, and how it went. Here rather than in either transport
/// because both count into it — MCP through its call-tool filter, UCP's REST
/// binding through an endpoint filter — and neither references the other's
/// plumbing.
///
/// <c>refused</c> is the shop saying no (a validation rule, a missing UCP
/// profile, an unknown id); <c>failed</c> is the tool breaking. The difference is
/// the whole point: a high refusal share means a tool's contract is wrong for an
/// agent, a failure is a bug.
/// </summary>
public static class AgentToolCalls
{
    public const string Answered = "answered";
    public const string Refused = "refused";
    public const string Failed = "failed";

    private static readonly Meter Meter = new(TelemetrySources.Agents);

    private static readonly Counter<long> Calls = Meter.CreateCounter<long>(
        "tendero.agent.tool_calls", unit: "{call}",
        description: "Calls from external agents to the MCP tools and the UCP catalogue operations.");

    public static void Record(string surface, string tool, string outcome) =>
        Calls.Add(1,
            new KeyValuePair<string, object?>(Actor.SurfaceTag, surface),
            new KeyValuePair<string, object?>("tool", tool),
            new KeyValuePair<string, object?>("outcome", outcome));
}
