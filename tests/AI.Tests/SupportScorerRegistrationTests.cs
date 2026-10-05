using ElGuerre.Tendero.AI.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>
/// The support scorer is registered once, for the claims pipeline and the
/// shopper's assistant alike (phase 12b): the model server's when the host was
/// given its address, a fixed score for a host that runs from a script, and
/// nothing otherwise — which each caller has an answer for.
/// </summary>
public sealed class SupportScorerRegistrationTests
{
    private static ISupportScorer? Registered(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)).Build();
        return new ServiceCollection().AddSupportScorer(configuration).BuildServiceProvider().GetService<ISupportScorer>();
    }

    [Fact]
    public void With_no_address_and_no_script_there_is_no_scorer() => Assert.Null(Registered());

    [Fact]
    public void With_the_model_servers_address_it_is_the_entailment_model() =>
        Assert.IsType<TeiNliSupportScorer>(Registered((SupportServiceCollectionExtensions.EndpointKey, "http://localhost:38084")));

    [Fact]
    public async Task A_host_that_runs_from_a_script_scores_every_pair_the_same()
    {
        var scorer = Assert.IsType<FixedSupportScorer>(Registered((SupportServiceCollectionExtensions.ScriptedKey, "0.95")));

        Assert.Equal(0.95, await scorer.ScoreAsync("material: lino", "Es de acero.", TestContext.Current.CancellationToken));
    }

    /// <summary>A real scorer is never replaced by the script's: an address always wins.</summary>
    [Fact]
    public void An_address_wins_over_a_script() =>
        Assert.IsType<TeiNliSupportScorer>(Registered(
            (SupportServiceCollectionExtensions.ScriptedKey, "0.95"), (SupportServiceCollectionExtensions.EndpointKey, "http://localhost:38084")));

    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    public void What_is_not_a_score_is_no_scorer(string scripted) =>
        Assert.Null(Registered((SupportServiceCollectionExtensions.ScriptedKey, scripted)));
}
