using System.Reflection;
using ElGuerre.Tendero.TrafficSim.Model;
using ElGuerre.Tendero.TrafficSim.Queries;
using ElGuerre.Tendero.TrafficSim.Scheduling;
using Xunit;

namespace ElGuerre.Tendero.TrafficSim.Tests;

/// <summary>
/// The declared model is a model: every number says where it came from, every
/// persona is a proper Markov chain, and what the chains add up to matches what
/// the sources publish. The transition probabilities are assumptions; the rates
/// they must produce are not.
/// </summary>
public sealed class ShopModelTests
{
    /// <summary>Enough walks that a 1 % rate is measured to a tenth of a point.</summary>
    private const int Walks = 200_000;

    [Fact]
    public void Every_parameter_names_its_source()
    {
        var unsourced = typeof(ShopModel)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Sourced<>))
            .Select(field => (field.Name, Source: (Source)field.FieldType.GetProperty(nameof(Sourced<int>.Source))!.GetValue(field.GetValue(null))!))
            .Where(entry => string.IsNullOrWhiteSpace(entry.Source.Name))
            .Select(entry => entry.Name);

        Assert.Empty(unsourced);
        Assert.All(ShopModel.Personas, persona =>
        {
            Assert.False(string.IsNullOrWhiteSpace(persona.Share.Source.Name));
            Assert.False(string.IsNullOrWhiteSpace(persona.TransitionsSource.Name));
        });
    }

    [Fact]
    public void Every_persona_is_a_markov_chain_that_ends()
    {
        foreach (var persona in ShopModel.Personas)
        {
            Assert.True(persona.Transitions.ContainsKey(persona.Entry), $"{persona.Name} has no edges out of its entry.");

            foreach (var (from, edges) in persona.Transitions)
            {
                Assert.InRange(edges.Sum(edge => edge.Probability), 1 - 1e-9, 1 + 1e-9);
                Assert.All(edges, edge => Assert.True(
                    edge.To == Step.Exit || persona.Transitions.ContainsKey(edge.To),
                    $"{persona.Name}: {from} leads to {edge.To}, which has no edges and is not the exit."));
            }
        }
    }

    [Fact]
    public void The_arrival_shares_add_up_to_everyone()
    {
        Assert.Equal(1, ShopModel.Personas.Sum(persona => persona.Share.Value), 9);
    }

    /// <summary>
    /// The published rates, emerging from the chains rather than being written
    /// into them: sessions that add to cart, carts that never become an order,
    /// sessions that end in one. Over human sessions, weighted by arrival share —
    /// agents do not buy until phase 11.
    /// </summary>
    [Fact]
    public void The_people_add_to_cart_abandon_and_convert_at_the_published_rates()
    {
        var humans = ShopModel.Personas.Where(persona => persona.Kind == PersonaKind.Human).ToArray();
        var total = humans.Sum(persona => persona.Share.Value);

        double added = 0, paid = 0, ordered = 0;
        foreach (var persona in humans)
        {
            var weight = persona.Share.Value / total;
            var journeys = Enumerable.Range(0, Walks)
                .Select(i => Journey.Draw(i, Seeds.ForSession(20260925, i), persona, _ => ["q"]))
                .ToArray();

            added += weight * journeys.Count(journey => journey.AddsToCart) / Walks;
            paid += weight * journeys.Count(journey => journey.AddsToCart && journey.Pays) / Walks;
            ordered += weight * journeys.Count(journey => journey.PlacesAnOrder) / Walks;
        }

        var abandonment = 1 - paid / added;
        TestContext.Current.TestOutputHelper?.WriteLine($"add to cart {added:P2}, abandonment {abandonment:P2}, conversion {ordered:P2}");

        Assert.InRange(added, ShopModel.AddToCartRate.Value - 0.015, ShopModel.AddToCartRate.Value + 0.015);
        Assert.InRange(abandonment, ShopModel.CartAbandonment.Value - 0.03, ShopModel.CartAbandonment.Value + 0.03);
        Assert.InRange(ordered, ShopModel.Conversion.Value.Low, ShopModel.Conversion.Value.High);
    }

    [Fact]
    public void The_same_seed_draws_the_same_journey_and_another_seed_does_not()
    {
        IReadOnlyList<string> Queries(string culture) => GoldenQueries.In(culture);

        var first = Journey.Draw(7, Seeds.ForSession(42, 7), ShopModel.Searcher, Queries);
        var again = Journey.Draw(7, Seeds.ForSession(42, 7), ShopModel.Searcher, Queries);
        var other = Enumerable.Range(0, 20)
            .Select(i => Journey.Draw(7, Seeds.ForSession(43 + i, 7), ShopModel.Searcher, Queries))
            .ToArray();

        Assert.Equal(first.Steps, again.Steps);
        Assert.Equal(first.Queries, again.Queries);
        Assert.Equal(first.CardDeclines, again.CardDeclines);
        Assert.Equal((first.Culture, first.GrantsConsent, first.AbandonmentReason), (again.Culture, again.GrantsConsent, again.AbandonmentReason));
        Assert.Contains(other, journey => !journey.Steps.SequenceEqual(first.Steps));
    }

    /// <summary>
    /// A session's seed depends on its index, so neighbours never share one, and
    /// across a long run the seeds spread like random ones: 31 bits over 100 000
    /// sessions give about two birthday collisions, not hundreds.
    /// </summary>
    [Fact]
    public void Sessions_of_one_run_get_spread_seeds()
    {
        var seeds = Enumerable.Range(0, 100_000).Select(i => Seeds.ForSession(20260925, i)).ToArray();

        Assert.All(seeds.Zip(seeds.Skip(1)), pair => Assert.NotEqual(pair.First, pair.Second));
        Assert.InRange(seeds.Length - seeds.Distinct().Count(), 0, 10);
    }

    [Fact]
    public void The_same_seed_draws_the_same_arrivals()
    {
        var day = TimeSpan.FromHours(1);

        var first = Arrivals.Draw(42, day, TimeSpan.Zero).Take(200).Select(a => (a.At, a.Persona.Name)).ToArray();
        var again = Arrivals.Draw(42, day, TimeSpan.Zero).Take(200).Select(a => (a.At, a.Persona.Name)).ToArray();

        Assert.Equal(first, again);
    }

    /// <summary>
    /// The thinning keeps the day's shape: over many simulated days, each hour
    /// gets arrivals in proportion to its weight in the profile.
    /// </summary>
    [Fact]
    public void Arrivals_follow_the_daily_profile()
    {
        var day = TimeSpan.FromHours(24);
        var days = 30;
        var counts = new int[24];

        foreach (var arrival in Arrivals.Draw(20260925, day, TimeSpan.Zero).TakeWhile(a => a.At < day * days))
            counts[(int)(arrival.At.TotalHours % 24)]++;

        var profile = ShopModel.HourlyProfile.Value;
        var perWeight = counts.Sum() / profile.Sum();

        for (var hour = 0; hour < 24; hour++)
            Assert.InRange(counts[hour], profile[hour] * perWeight * 0.85, profile[hour] * perWeight * 1.15);
    }

    [Fact]
    public void The_arrivals_mix_the_personas_by_their_shares()
    {
        var arrivals = Arrivals.Draw(20260925, TimeSpan.FromHours(1), TimeSpan.Zero).Take(50_000).ToArray();

        foreach (var persona in ShopModel.Personas)
        {
            var share = arrivals.Count(arrival => arrival.Persona == persona) / (double)arrivals.Length;
            Assert.InRange(share, persona.Share.Value - 0.01, persona.Share.Value + 0.01);
        }
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void The_queries_are_the_golden_sets(string culture)
    {
        Assert.True(GoldenQueries.In(culture).Count >= 20, $"Only {GoldenQueries.In(culture).Count} {culture} queries embedded.");
    }
}
