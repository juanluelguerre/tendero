namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// Tags for the process's health checks. A <see cref="Private"/> check reports
/// through a route of its own, behind the shopkeeper's policy, and never through
/// the open <c>/health</c>: what it says is the shop's business, and a part that
/// is switched off on purpose must not make the whole process look unhealthy.
/// </summary>
public static class HealthTags
{
    public const string Private = "private";

    /// <summary>The stages of search (R-9, D-7).</summary>
    public const string Search = "search";
}
