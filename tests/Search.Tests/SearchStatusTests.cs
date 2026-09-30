using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Health;
using ElGuerre.Tendero.Tests;
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

    private sealed class FixedLayer(VectorLayerStatus status) : IVectorLayer
    {
        public Task<VectorLayerStatus> CheckAsync(CancellationToken ct = default) => Task.FromResult(status);
    }

    private sealed class Reranker(bool answers) : IReranker
    {
        public Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> texts, CancellationToken ct = default) =>
            answers ? Task.FromResult<IReadOnlyList<double>>([1d]) : throw new HttpRequestException("connection refused");
    }

    [Theory]
    [InlineData(true, nameof(StageState.Up))]
    [InlineData(false, nameof(StageState.Down))]
    public async Task Meaning_switched_on_is_up_when_it_answers_and_down_when_it_does_not(bool available, string state)
    {
        var check = new MeaningHealthCheck(new FixedLayer(new VectorLayerStatus(IsConfigured: true, available, "the vector store")));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(state, result.Data[SearchHealth.StateKey]);
    }

    [Theory]
    [InlineData(true, nameof(StageState.Up))]
    [InlineData(false, nameof(StageState.Down))]
    public async Task Reranking_is_up_when_it_scores_and_down_when_it_does_not(bool answers, string state)
    {
        var check = new RerankingHealthCheck(new Reranker(answers));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(state, result.Data[SearchHealth.StateKey]);
    }

    /// <summary>
    /// The publisher reads every round of checks: each search check lands on its
    /// stage, one with no state it can read is down, and the other checks of the
    /// API are none of its business.
    /// </summary>
    [Fact]
    public async Task Each_check_lands_on_its_stage_and_the_rest_are_left_alone()
    {
        var memory = new SearchStatusMemory();
        var publisher = new SearchStatusPublisher(memory, new TestClock(First));
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["search-words"] = Entry(SearchHealth.Result(StageState.Up)),
                ["search-meaning"] = Entry(SearchHealth.Result(StageState.Off, "switched off")),
                ["search-reranking"] = Entry(HealthCheckResult.Unhealthy("timed out")),
                ["postgres"] = Entry(HealthCheckResult.Unhealthy("not ours")),
            },
            TimeSpan.Zero);

        await publisher.PublishAsync(report, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new StageStatus(SearchStage.Words, StageState.Up, First, null),
                new StageStatus(SearchStage.Meaning, StageState.Off, First, "switched off"),
                new StageStatus(SearchStage.Reranking, StageState.Down, First, "timed out"),
            ],
            memory.Current().Stages);
    }

    private static HealthReportEntry Entry(HealthCheckResult result) =>
        new(result.Status, result.Description, TimeSpan.Zero, result.Exception, result.Data);
}
