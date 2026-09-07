namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// The noise a picture starts as, and the reason regenerating one product twice
/// gives the same picture twice.
///
/// **`System.Random` is not used here on purpose.** Its algorithm is not part of
/// the contract and has changed between .NET versions, so a tool that promised
/// `--rng-seed 1234` reproduces would quietly stop reproducing after an SDK bump
/// — and nothing would say so, because a different picture is not an error.
/// `xoshiro256**` is forty lines and the guarantee becomes ours.
///
/// Say the other half plainly, because somebody will try it: **this does not
/// reproduce PyTorch's samples for the same integer.** A seed found on a website
/// will not give that website's image here. That is fine; it is only a promise
/// about this tool being consistent with itself.
/// </summary>
public sealed class LatentNoise
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    private double _spare;
    private bool _hasSpare;

    public LatentNoise(ulong seed)
    {
        // SplitMix64 to spread one number into four words. A generator seeded
        // with mostly-zero state produces mostly-zero output for a while.
        _s0 = Mix(ref seed);
        _s1 = Mix(ref seed);
        _s2 = Mix(ref seed);
        _s3 = Mix(ref seed);
    }

    /// <summary>
    /// The seed for a product, derived from its id so that regenerating one is a
    /// repeatable act rather than a roll.
    ///
    /// FNV-1a written out rather than `string.GetHashCode`, which is randomised
    /// per process: with it, the same product would get a different picture on
    /// every run of the same command.
    /// </summary>
    public static ulong SeedFor(string itemId)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;

        var hash = offset;

        foreach (var character in itemId)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash;
    }

    /// <summary>A block of standard normal noise, which is what the latent starts
    /// as before the schedule scales it.</summary>
    public float[] Normal(int count)
    {
        var values = new float[count];

        for (var index = 0; index < count; index++)
            values[index] = (float)NextNormal();

        return values;
    }

    /// <summary>Box-Muller, which makes two samples at a time; the second is kept
    /// for the next call rather than thrown away.</summary>
    private double NextNormal()
    {
        if (_hasSpare)
        {
            _hasSpare = false;
            return _spare;
        }

        double first, second, square;

        do
        {
            first = (NextDouble() * 2.0) - 1.0;
            second = (NextDouble() * 2.0) - 1.0;
            square = (first * first) + (second * second);
        }
        while (square >= 1.0 || square == 0.0);

        var factor = Math.Sqrt(-2.0 * Math.Log(square) / square);

        _spare = second * factor;
        _hasSpare = true;

        return first * factor;
    }

    private double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));

    private ulong Next()
    {
        var result = Rotate(_s1 * 5, 7) * 9;
        var shifted = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= shifted;
        _s3 = Rotate(_s3, 45);

        return result;
    }

    private static ulong Rotate(ulong value, int count) => (value << count) | (value >> (64 - count));

    private static ulong Mix(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15;

        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;

        return z ^ (z >> 31);
    }
}
