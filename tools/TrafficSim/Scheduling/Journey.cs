using ElGuerre.Tendero.TrafficSim.Model;

namespace ElGuerre.Tendero.TrafficSim.Scheduling;

/// <summary>One step of a drawn journey, and how long to wait before it.</summary>
public sealed record JourneyStep(Step Step, TimeSpan ThinkTime);

/// <summary>
/// A session's whole plan, drawn before a single request is made: which persona,
/// which steps, which choices at each — the query, whether the card works,
/// whether the person says yes to analytics. A driver only carries it out.
///
/// Drawing it all up front is what makes a run reproducible: the same seed gives
/// the same journey whatever the shop answers, and a slow page cannot change the
/// next step.
/// </summary>
public sealed record Journey(
    long Index,
    int Seed,
    Persona Persona,
    string Culture,
    bool GrantsConsent,
    IReadOnlyList<JourneyStep> Steps,
    IReadOnlyList<string> Queries,
    IReadOnlyList<bool> CardDeclines,
    string? AbandonmentReason,
    string? ReturnReason = null)
{
    /// <summary>Hard stop for a chain that keeps looping: no real visit is a hundred pages.</summary>
    public const int MaxSteps = 40;

    public bool AddsToCart => Steps.Any(step => step.Step == Step.AddToCart);

    public bool Pays => Steps.Any(step => step.Step == Step.Pay);

    /// <summary>
    /// Pays and is not declined for good: every decline is followed by a retry
    /// or by the end of the session.
    /// </summary>
    public bool PlacesAnOrder => Pays && (CardDeclines.Count == 0 || !CardDeclines[^1]);

    public static Journey Draw(long index, int seed, Persona persona, Func<string, IReadOnlyList<string>> queriesIn)
    {
        var random = new Random(seed);
        var culture = random.NextDouble() < ShopModel.EnglishShare.Value ? "en" : "es";
        var grantsConsent = random.NextDouble() < ShopModel.ConsentGranted.Value;
        var steps = new List<JourneyStep>();
        var current = persona.Entry;

        while (current != Step.Exit && steps.Count < MaxSteps)
        {
            steps.Add(new JourneyStep(current, Think(random, persona.Kind, first: steps.Count == 0)));
            current = Next(random, persona, current);
        }

        // One query per search the journey makes, drawn from the golden set so
        // the pulse and SearchEval talk about the same questions.
        var searches = steps.Count(step => step.Step is Step.Search or Step.McpSearch or Step.UcpSearch);
        var queries = queriesIn(culture);
        var drawn = queries.Count == 0
            ? []
            : Enumerable.Range(0, searches).Select(_ => queries[random.Next(queries.Count)]).ToList();

        // At Pay: declined or not, and after a decline, one more try or none.
        var declines = new List<bool>();
        if (steps.Any(step => step.Step == Step.Pay))
        {
            var declined = random.NextDouble() < ShopModel.CardDeclined.Value;
            declines.Add(declined);
            if (declined && random.NextDouble() < ShopModel.RetryAfterDecline.Value)
                declines.Add(random.NextDouble() < ShopModel.CardDeclined.Value);
        }

        var abandoned = steps.Any(step => step.Step == Step.AddToCart) && !steps.Any(step => step.Step == Step.Pay);
        var returns = steps.Any(step => step.Step == Step.RequestReturn);

        return new Journey(
            index, seed, persona,
            culture, grantsConsent, steps, drawn, declines,
            AbandonmentReason: abandoned ? Weighted(random, ShopModel.AbandonmentReasons.Value) : null,
            ReturnReason: returns ? Weighted(random, ShopModel.ReturnReasons.Value) : null);
    }

    private static Step Next(Random random, Persona persona, Step from)
    {
        if (!persona.Transitions.TryGetValue(from, out var edges))
            return Step.Exit;

        var roll = random.NextDouble();
        foreach (var edge in edges)
        {
            roll -= edge.Probability;
            if (roll < 0)
                return edge.To;
        }

        return edges[^1].To;
    }

    /// <summary>TPC-W's think time: exponential, truncated at ten times its mean.</summary>
    private static TimeSpan Think(Random random, PersonaKind kind, bool first)
    {
        if (first)
            return TimeSpan.Zero;

        var mean = kind == PersonaKind.Agent ? ShopModel.AgentPauseMeanSeconds.Value : ShopModel.ThinkTimeMeanSeconds.Value;
        var seconds = Math.Min(-mean * Math.Log(1 - random.NextDouble()), ShopModel.ThinkTimeCapSeconds.Value);
        return TimeSpan.FromSeconds(seconds);
    }

    private static string Weighted(Random random, IReadOnlyList<(string Name, double Weight)> options)
    {
        var roll = random.NextDouble() * options.Sum(option => option.Weight);
        foreach (var (name, weight) in options)
        {
            roll -= weight;
            if (roll < 0)
                return name;
        }

        return options[^1].Name;
    }
}
