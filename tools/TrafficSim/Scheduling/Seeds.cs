namespace ElGuerre.Tendero.TrafficSim.Scheduling;

/// <summary>
/// One seed per run, and one per session derived from it (ADR 0037, decision 3).
///
/// A session's seed is a function of the run's seed and the session's index and
/// of nothing else — not of when it started, nor of what ran beside it — so
/// concurrency can never change what a session does. SplitMix64's finaliser
/// spreads neighbouring indices into unrelated seeds; it is the mixer the JDK and
/// xoshiro use for exactly this.
/// </summary>
public static class Seeds
{
    public static int ForSession(int runSeed, long index) =>
        (int)(Mix(unchecked((ulong)(uint)runSeed * 0x9E3779B97F4A7C15UL + (ulong)index)) >> 33);

    /// <summary>The arrivals' own stream, so drawing a session never shifts when the next one comes.</summary>
    public static int ForArrivals(int runSeed) => (int)(Mix(unchecked((ulong)(uint)runSeed ^ 0xA5A5A5A5A5A5A5A5UL)) >> 33);

    private static ulong Mix(ulong z)
    {
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }
}
