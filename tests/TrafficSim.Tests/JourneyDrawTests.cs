using ElGuerre.Tendero.TrafficSim.Model;
using ElGuerre.Tendero.TrafficSim.Scheduling;
using Xunit;

namespace ElGuerre.Tendero.TrafficSim.Tests;

/// <summary>
/// What a journey draws on top of its chain — the language, the consent, the
/// card, the reasons, the pauses — each at the rate the model declares.
///
/// The published rates in <see cref="ShopModelTests"/> are totals over whole
/// sessions, and their tolerances are wide enough to hide any one of these: the
/// phase 9b Stryker run found nearly every choice below surviving. So each is
/// checked on its own here, over tiny personas built for it — a chain that goes
/// straight to Pay measures the card in every walk instead of one in forty.
/// </summary>
public sealed class JourneyDrawTests
{
    private const int Draws = 20_000;

    private static readonly Source Test = Source.Assumption("A persona built for one test.");

    private static Persona Chain(PersonaKind kind, Step entry, params (Step From, Step To, double P)[] edges) =>
        new("test", kind, new(0, Test), entry,
            edges.GroupBy(edge => edge.From).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Transition>)[.. group.Select(edge => new Transition(edge.To, edge.P))]),
            Test);

    // Pay after another step, so a check on "any step pays" cannot pass as "every step does".
    private static readonly Persona Payer = Chain(PersonaKind.Human, Step.Checkout, (Step.Checkout, Step.Pay, 1), (Step.Pay, Step.Exit, 1));

    private static Journey[] Draw(Persona persona, int count = Draws, Func<string, IReadOnlyList<string>>? queries = null) =>
        [.. Enumerable.Range(0, count).Select(i => Journey.Draw(i, Seeds.ForSession(20260926, i), persona, queries ?? (_ => ["q"])))];

    private static double Share<T>(IReadOnlyCollection<T> items, Func<T, bool> which) =>
        items.Count(which) / (double)items.Count;

    [Fact]
    public void Language_and_consent_follow_their_shares()
    {
        var journeys = Draw(Payer);

        Assert.Equal(["en", "es"], journeys.Select(journey => journey.Culture).Distinct().Order());
        Assert.InRange(Share(journeys, journey => journey.Culture == "en"), ShopModel.EnglishShare.Value - 0.02, ShopModel.EnglishShare.Value + 0.02);
        Assert.InRange(Share(journeys, journey => journey.GrantsConsent), ShopModel.ConsentGranted.Value - 0.02, ShopModel.ConsentGranted.Value + 0.02);
    }

    /// <summary>
    /// At Pay the card is declined at the model's rate; after a decline, half
    /// try once more; and a second try only ever follows a decline.
    /// </summary>
    [Fact]
    public void The_card_is_declined_and_retried_at_the_model_rates()
    {
        var journeys = Draw(Payer);
        var declined = journeys.Where(journey => journey.CardDeclines[0]).ToArray();

        Assert.All(journeys, journey => Assert.InRange(journey.CardDeclines.Count, 1, 2));
        Assert.InRange(declined.Length / (double)Draws, ShopModel.CardDeclined.Value - 0.01, ShopModel.CardDeclined.Value + 0.01);
        Assert.InRange(Share(declined, journey => journey.CardDeclines.Count == 2), ShopModel.RetryAfterDecline.Value - 0.05, ShopModel.RetryAfterDecline.Value + 0.05);
        Assert.All(journeys.Where(journey => !journey.CardDeclines[0]), journey => Assert.Single(journey.CardDeclines));

        var retries = declined.Where(journey => journey.CardDeclines.Count == 2).ToArray();
        Assert.InRange(Share(retries, journey => journey.CardDeclines[1]), ShopModel.CardDeclined.Value - 0.04, ShopModel.CardDeclined.Value + 0.04);
    }

    [Fact]
    public void A_journey_that_never_pays_draws_no_card()
    {
        var browser = Chain(PersonaKind.Human, Step.Home, (Step.Home, Step.Exit, 1));

        Assert.All(Draw(browser, 200), journey => Assert.Empty(journey.CardDeclines));
    }

    /// <summary>
    /// A reason is drawn only for what needs one — a cart left without paying, a
    /// return — and in the proportions the sources publish.
    /// </summary>
    [Fact]
    public void Reasons_are_drawn_only_when_they_apply_and_by_their_weights()
    {
        var abandoner = Chain(PersonaKind.Human, Step.Product, (Step.Product, Step.AddToCart, 1), (Step.AddToCart, Step.Exit, 1));
        var buyer = Chain(PersonaKind.Human, Step.AddToCart, (Step.AddToCart, Step.Pay, 1), (Step.Pay, Step.Exit, 1));
        var returner = Chain(PersonaKind.Human, Step.RequestReturn, (Step.RequestReturn, Step.Exit, 1));

        var abandoned = Draw(abandoner);
        Assert.All(abandoned, journey => Assert.Null(journey.ReturnReason));
        AssertWeighted(abandoned.Select(journey => journey.AbandonmentReason!).ToArray(), ShopModel.AbandonmentReasons.Value);

        Assert.All(Draw(buyer, 200), journey => Assert.Null(journey.AbandonmentReason));

        var returned = Draw(returner);
        Assert.All(returned, journey => Assert.Null(journey.AbandonmentReason));
        AssertWeighted(returned.Select(journey => journey.ReturnReason!).ToArray(), ShopModel.ReturnReasons.Value);
    }

    /// <summary>
    /// The first page is opened at once; after that a person thinks for TPC-W's
    /// seven seconds on average and an agent pauses for a second and a half,
    /// both exponential and never past the cap.
    /// </summary>
    [Theory]
    [InlineData(PersonaKind.Human)]
    [InlineData(PersonaKind.Agent)]
    public void Think_time_is_exponential_capped_and_absent_before_the_first_step(PersonaKind kind)
    {
        var looping = Chain(kind, Step.Home, (Step.Home, Step.Home, 1));
        var journeys = Draw(looping, 1000);
        var pauses = journeys.SelectMany(journey => journey.Steps.Skip(1)).Select(step => step.ThinkTime.TotalSeconds).ToArray();
        var mean = kind == PersonaKind.Agent ? ShopModel.AgentPauseMeanSeconds.Value : ShopModel.ThinkTimeMeanSeconds.Value;

        Assert.All(journeys, journey => Assert.Equal(TimeSpan.Zero, journey.Steps[0].ThinkTime));
        Assert.All(pauses, seconds => Assert.InRange(seconds, 0, ShopModel.ThinkTimeCapSeconds.Value));
        Assert.InRange(pauses.Average(), mean * 0.95, mean * 1.05);
    }

    [Fact]
    public void A_chain_that_never_exits_stops_at_the_step_limit()
    {
        var looping = Chain(PersonaKind.Human, Step.Home, (Step.Home, Step.Home, 1));

        Assert.All(Draw(looping, 10), journey => Assert.Equal(Journey.MaxSteps, journey.Steps.Count));
    }

    /// <summary>One query per search, of whichever kind, drawn from the list given; none when the list is empty.</summary>
    [Fact]
    public void Every_search_draws_one_query_from_the_list()
    {
        var searcher = Chain(PersonaKind.Agent, Step.McpSearch,
            (Step.McpSearch, Step.UcpSearch, 0.5), (Step.McpSearch, Step.Exit, 0.5),
            (Step.UcpSearch, Step.Search, 0.5), (Step.UcpSearch, Step.Exit, 0.5),
            (Step.Search, Step.Exit, 1));
        string[] queries = ["a", "b", "c"];

        Assert.All(Draw(searcher, 500, _ => queries), journey =>
        {
            Assert.Equal(journey.Steps.Count, journey.Queries.Count);
            Assert.All(journey.Queries, query => Assert.Contains(query, queries));
        });
        Assert.All(Draw(searcher, 50, _ => []), journey => Assert.Empty(journey.Queries));
    }

    /// <summary>
    /// The mixer is SplitMix64's finaliser, pinned to the reference generator's
    /// first two outputs from state 0 (0xE220A8397B1DCDAF, 0x6E789E6AA1B965F4):
    /// a session seed is those outputs' top 31 bits. The arrivals' stream is
    /// pinned as it is, because changing it would change every run on record.
    /// </summary>
    [Fact]
    public void Seeds_are_splitmix64()
    {
        Assert.Equal(0xE220A8397B1DCDAFUL >> 33, (ulong)Seeds.ForSession(0, unchecked((long)0x9E3779B97F4A7C15UL)));
        Assert.Equal(0x6E789E6AA1B965F4UL >> 33, (ulong)Seeds.ForSession(2, 0));
        Assert.Equal(173906560, Seeds.ForArrivals(42));
    }

    [Fact]
    public void Arrivals_are_numbered_from_zero_without_gaps()
    {
        var indices = Arrivals.Draw(42, TimeSpan.FromHours(1), TimeSpan.Zero).Take(100).Select(arrival => arrival.Index);

        Assert.Equal(Enumerable.Range(0, 100).Select(i => (long)i), indices);
    }

    /// <summary>A day compressed into an hour: half an hour in is noon, and a quarter of an hour in, from six, is noon too.</summary>
    [Fact]
    public void The_rate_reads_the_simulated_hour()
    {
        var noon = ShopModel.HourlyProfile.Value[12];

        Assert.Equal(noon, Arrivals.RateAt(TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.Zero));
        Assert.Equal(noon, Arrivals.RateAt(TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(6)));
    }

    private static void AssertWeighted(string[] drawn, IReadOnlyList<(string Name, double Weight)> options)
    {
        var total = options.Sum(option => option.Weight);

        foreach (var (name, weight) in options)
            Assert.InRange(Share(drawn, reason => reason == name), weight / total - 0.02, weight / total + 0.02);
    }
}
