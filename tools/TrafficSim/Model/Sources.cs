namespace ElGuerre.Tendero.TrafficSim.Model;

/// <summary>
/// How much a number can be trusted (ADR 0037): the grade is printed beside the
/// parameter, so the article can say which of the dashboard's shapes come from a
/// study and which from us.
/// </summary>
public enum Grade
{
    /// <summary>A study or a specification that publishes its method.</summary>
    Primary,

    /// <summary>A reputable summary of primary work, read second hand.</summary>
    Secondary,

    /// <summary>A vendor's aggregate, or a figure repeated without its method.</summary>
    Low,

    /// <summary>Ours. Said so, and chosen to be plausible rather than true.</summary>
    Assumption
}

/// <summary>Where a parameter comes from.</summary>
public sealed record Source(string Name, Grade Grade, string? Url = null)
{
    public static Source Assumption(string why) => new(why, Grade.Assumption);
}

/// <summary>A value that cannot be declared without saying where it came from.</summary>
public sealed record Sourced<T>(T Value, Source Source);
