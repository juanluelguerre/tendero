namespace ElGuerre.Tendero.TrafficSim.Model;

public enum PersonaKind
{
    /// <summary>In a headless browser, through the storefront.</summary>
    Human,

    /// <summary>Over a protocol, with no browser: MCP or UCP.</summary>
    Agent
}

/// <summary>One edge of the graph: from a step, the chance of going to another.</summary>
public sealed record Transition(Step To, double Probability);

/// <summary>
/// One persona: a discrete-time Markov chain over <see cref="Step"/>, entered at
/// <see cref="Entry"/> and left at <see cref="Step.Exit"/>. The transitions are
/// the whole behaviour; nothing a driver does may decide where a session goes.
/// </summary>
public sealed record Persona(
    string Name,
    PersonaKind Kind,
    Sourced<double> Share,
    Step Entry,
    IReadOnlyDictionary<Step, IReadOnlyList<Transition>> Transitions,
    Source TransitionsSource);
