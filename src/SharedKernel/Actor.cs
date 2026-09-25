namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// Who acted, and through which door — the two labels every business metric
/// carries (ADR 0036), so the *Shop pulse* can split a funnel between people and
/// agents without an id ever becoming a label.
///
/// **Computed on the server, never taken from the client.** The surface is the
/// route the request arrived on, and an MCP or UCP caller is an agent by
/// definition — both are anonymous by decision, so the principal alone would
/// call them people. WebMCP is not a surface here, and cannot be: it inherits
/// the shopper's own session (CLAUDE.md, "three agent surfaces"), so to the
/// server it IS the web. Its evidence is the browser's `webmcp_tool_invoked`.
/// </summary>
public sealed record Actor(string Kind, string Surface)
{
    /// <summary>Background work: the outbox worker, the seeders, an evaluation run.</summary>
    public static readonly Actor System = new(ActorKinds.System, Surfaces.Internal);

    public const string KindTag = "actor.kind";
    public const string SurfaceTag = "surface";

    public KeyValuePair<string, object?> KindLabel => new(KindTag, Kind);
    public KeyValuePair<string, object?> SurfaceLabel => new(SurfaceTag, Surface);
}

public static class ActorKinds
{
    public const string Human = "human";
    public const string Agent = "agent";
    public const string System = "system";
}

public static class Surfaces
{
    public const string Web = "web";
    public const string Mcp = "mcp";
    public const string Ucp = "ucp";
    public const string Internal = "internal";
}

/// <summary>
/// Where the current <see cref="Actor"/> comes from. A port for the reason
/// <see cref="IPrincipalAccessor"/> is one: it has to answer inside the worker,
/// where there is no request. The API derives it from the request; everywhere
/// else it is <see cref="Actor.System"/>.
/// </summary>
public interface IActorAccessor
{
    Actor Current { get; }
}

public sealed class SystemActorAccessor : IActorAccessor
{
    public Actor Current => Actor.System;
}
