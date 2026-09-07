namespace ElGuerre.Tendero.Tests;

/// <summary>
/// A fixed clock that only moves when it is told to. Hand-written rather than
/// bringing in <c>Microsoft.Extensions.TimeProvider.Testing</c>: it is fifteen
/// lines, and the repository's dependency budget is deliberately small.
///
/// It is linked as a file into each test project that needs it. A shared project
/// for one class would be more ceremony than code.
/// </summary>
public sealed class TestClock(DateTimeOffset? start = null) : TimeProvider
{
    /// <summary>
    /// A concrete, readable instant rather than <c>UtcNow</c>: when a test fails,
    /// the message has to contain a date that is recognisably fixed.
    /// </summary>
    public static readonly DateTimeOffset Default = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

    private DateTimeOffset now = start ?? Default;

    public override DateTimeOffset GetUtcNow() => this.now;

    /// <summary>Moves the clock and returns the new instant.</summary>
    public DateTimeOffset Advance(TimeSpan by) => this.now = this.now.Add(by);
}
