using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// Search's status for the shopkeeper (R-9, D-7): each stage — words, meaning,
/// reranking — as the shopper gets it, and since when. A stage that never changed
/// is in its state since the first check; one that flaps says its latest change.
/// </summary>
public sealed class SearchStatusTests
{
    private static readonly DateTimeOffset First = new(2026, 9, 30, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_stage_that_never_changed_is_in_its_state_since_the_first_check()
    {
        var memory = new SearchStatusMemory();
        memory.Record(SearchStage.Words, StageState.Up, null, First);
        memory.Record(SearchStage.Words, StageState.Up, null, First.AddMinutes(1));

        Assert.Equal(new StageStatus(SearchStage.Words, StageState.Up, First, null), memory.Current().Stages.Single());
    }

    [Fact]
    public void A_stage_that_went_down_says_since_when_and_why()
    {
        var memory = new SearchStatusMemory();
        memory.Record(SearchStage.Meaning, StageState.Up, null, First);
        memory.Record(SearchStage.Meaning, StageState.Down, "the vector store does not answer", First.AddMinutes(40));

        Assert.Equal(
            new StageStatus(SearchStage.Meaning, StageState.Down, First.AddMinutes(40), "the vector store does not answer"),
            memory.Current().Stages.Single());
    }

    [Fact]
    public void A_stage_that_flaps_says_its_latest_change()
    {
        var memory = new SearchStatusMemory();
        memory.Record(SearchStage.Meaning, StageState.Down, null, First);
        memory.Record(SearchStage.Meaning, StageState.Up, null, First.AddSeconds(20));
        memory.Record(SearchStage.Meaning, StageState.Down, null, First.AddSeconds(40));

        Assert.Equal(First.AddSeconds(40), memory.Current().Stages.Single().Since);
    }

    [Fact]
    public void The_stages_are_answered_in_the_order_a_search_runs_them()
    {
        var memory = new SearchStatusMemory();
        memory.Record(SearchStage.Reranking, StageState.Off, null, First);
        memory.Record(SearchStage.Words, StageState.Up, null, First);
        memory.Record(SearchStage.Meaning, StageState.Off, null, First);

        Assert.Equal([SearchStage.Words, SearchStage.Meaning, SearchStage.Reranking], memory.Current().Stages.Select(stage => stage.Stage));
    }

    /// <summary>Switched off is a choice of this process, not a failure: off, not down.</summary>
    [Fact]
    public async Task Meaning_switched_off_is_off_not_down()
    {
        var check = new MeaningHealthCheck(new NoVectorLayer());

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(nameof(StageState.Off), result.Data[SearchHealth.StateKey]);
    }

    [Fact]
    public async Task Reranking_that_is_not_configured_is_off()
    {
        var check = new RerankingHealthCheck(new NoReranker());

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(nameof(StageState.Off), result.Data[SearchHealth.StateKey]);
    }
}
