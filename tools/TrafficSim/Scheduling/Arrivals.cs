using ElGuerre.Tendero.TrafficSim.Model;

namespace ElGuerre.Tendero.TrafficSim.Scheduling;

/// <summary>One session's arrival: when, relative to the run's start, and which persona.</summary>
public sealed record Arrival(long Index, TimeSpan At, Persona Persona);

/// <summary>
/// Arrivals as a non-homogeneous Poisson process (ADR 0037): the rate follows
/// <see cref="ShopModel.HourlyProfile"/> over a simulated day compressed into
/// <paramref name="dayLength"/> of real time. Drawn by thinning (Lewis and
/// Shedler): candidates at the peak rate, each kept with the probability that
/// the hour's rate bears to the peak's.
///
/// The persona is drawn here too, from the arrivals' own stream, so which
/// persona arrives never depends on what an earlier session did.
/// </summary>
public static class Arrivals
{
    public static IEnumerable<Arrival> Draw(int runSeed, TimeSpan dayLength, TimeSpan startOfDay)
    {
        var random = new Random(Seeds.ForArrivals(runSeed));
        var peakPerSecond = ShopModel.PeakSessionsPerMinute.Value / 60;
        var at = TimeSpan.Zero;
        long index = 0;

        while (true)
        {
            at += TimeSpan.FromSeconds(-Math.Log(1 - random.NextDouble()) / peakPerSecond);

            var keep = random.NextDouble() < RateAt(at, dayLength, startOfDay);
            var persona = Pick(random);

            if (keep)
                yield return new Arrival(index++, at, persona);
        }
    }

    /// <summary>The hour's share of the peak, at a real offset into the run.</summary>
    public static double RateAt(TimeSpan at, TimeSpan dayLength, TimeSpan startOfDay)
    {
        var simulated = startOfDay + TimeSpan.FromTicks((long)(at.Ticks * (TimeSpan.FromDays(1).Ticks / (double)dayLength.Ticks)));
        var hour = (int)(simulated.TotalHours % 24);
        return ShopModel.HourlyProfile.Value[hour];
    }

    private static Persona Pick(Random random)
    {
        var roll = random.NextDouble() * ShopModel.Personas.Sum(persona => persona.Share.Value);
        foreach (var persona in ShopModel.Personas)
        {
            roll -= persona.Share.Value;
            if (roll < 0)
                return persona;
        }

        return ShopModel.Personas[^1];
    }
}
