namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// The system's <c>ActivitySource</c> and <c>Meter</c> names, in one place.
/// Adding a slice with I/O means adding its name here — if it does not appear in
/// <see cref="All"/>, OpenTelemetry does not register it and the slice stops
/// being traced without anything failing.
///
/// They live in SharedKernel and not in ServiceDefaults because both ends need
/// them: whoever OPENS the span (a Catalog or Search slice) and whoever
/// REGISTERS it (the composition root). While they were literals in each slice,
/// the agreement was a string coincidence no compiler checked, and the penalty
/// for a typo was an empty trace in the dashboard.
/// </summary>
public static class TelemetrySources
{
    public const string Accounts = "ElGuerre.Tendero.Accounts";
    public const string Catalog = "ElGuerre.Tendero.Catalog";

    /// <summary>The sales channels Tendero publishes to, and the orders they take (phase 16, S3).</summary>
    public const string Channels = "ElGuerre.Tendero.Channels";

    public const string Inventory = "ElGuerre.Tendero.Inventory";
    public const string Ordering = "ElGuerre.Tendero.Ordering";
    public const string Pricing = "ElGuerre.Tendero.Pricing";
    public const string Search = "ElGuerre.Tendero.Search";
    public const string Knowledge = "ElGuerre.Tendero.Knowledge";

    /// <summary>Steward's proposals and their decisions (phase 12).</summary>
    public const string Assist = "ElGuerre.Tendero.Assist";

    /// <summary>Every model call, whoever makes it (phase 12, ADR 0053).</summary>
    public const string AI = "ElGuerre.Tendero.AI";
    public const string Outbox = "ElGuerre.Tendero.Outbox";

    /// <summary>The agent transports, MCP and UCP: what external agents call.</summary>
    public const string Agents = "ElGuerre.Tendero.Agents";

    /// <summary>A store's migration as one operation across the contexts (phase 16, P16-21), composed by the host.</summary>
    public const string Migration = "ElGuerre.Tendero.Migration";

    /// <summary>What leaves the shop: feeds and exports (phase 16, E-1).</summary>
    public const string Exports = "ElGuerre.Tendero.Exports";

    /// <summary>
    /// The dispatcher pipeline itself. It gets a source because the audit step
    /// is the one place that swallows an exception on purpose, and a swallowed
    /// exception with nowhere to surface is indistinguishable from one that
    /// never happened.
    /// </summary>
    public const string SharedKernel = "ElGuerre.Tendero.SharedKernel";

    public static readonly string[] All =
        [Accounts, AI, Assist, Catalog, Channels, Inventory, Knowledge, Ordering, Pricing, Search, Outbox, Agents, Migration, Exports, SharedKernel];
}
