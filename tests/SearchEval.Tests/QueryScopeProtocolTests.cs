using ElGuerre.Tendero.SearchEval.Scope;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// The selection and acceptance rules of layer B's protocol, declared in the
/// notebook on 2026-09-17 before anything was measured. Pure, so the rules the
/// run is judged by are proven before the run exists.
/// </summary>
public sealed class QueryScopeProtocolTests
{
    [Fact]
    public void The_margin_that_recognises_most_without_a_false_not_sold_is_chosen() =>
        Assert.Equal(0.05, QueryScopeProtocol.Choose(
        [
            new(0.02, OutRecognised: 18, OutTotal: 20, InMarkedOut: 1),
            new(0.05, 15, 20, 0),
            new(0.08, 12, 20, 0),
            new(0.12, 9, 20, 0)
        ]));

    [Fact]
    public void A_tie_goes_to_the_larger_margin() =>
        Assert.Equal(0.08, QueryScopeProtocol.Choose([new(0.05, 12, 20, 0), new(0.08, 12, 20, 0)]));

    [Fact]
    public void When_every_margin_tells_a_real_query_it_is_not_sold_nothing_is_chosen() =>
        Assert.Null(QueryScopeProtocol.Choose([new(0.02, 18, 20, 2), new(0.12, 5, 20, 1)]));

    [Theory]
    [InlineData(10, 20, 0, 0, true)]
    [InlineData(9, 20, 0, 0, false)]    // under half recognised
    [InlineData(20, 20, 1, 0, false)]   // one false not-sold in scope-test
    [InlineData(20, 20, 0, 1, false)]   // one false not-sold among the golden queries
    public void Acceptance_needs_no_false_not_sold_anywhere_and_half_recognised(int recognised, int total, int testInOut, int goldenInOut, bool accepted) =>
        Assert.Equal(accepted, QueryScopeProtocol.Accepts(
            [new(0.05, recognised, total, testInOut), new(0.05, 20, 20, 0)],
            [goldenInOut, 0]));

    [Fact]
    public void Nothing_measured_is_never_accepted() =>
        Assert.False(QueryScopeProtocol.Accepts([], []));

    /// <summary>
    /// Half a measurement is not one either. With only one side empty, the other
    /// side's <c>All</c> is vacuously true, so each emptiness check is the only
    /// thing refusing it (Stryker survivors, 2026-09-17).
    /// </summary>
    [Fact]
    public void A_scope_test_without_golden_counts_is_not_accepted() =>
        Assert.False(QueryScopeProtocol.Accepts([new(0.05, 20, 20, 0)], []));

    [Fact]
    public void Golden_counts_without_a_scope_test_are_not_accepted() =>
        Assert.False(QueryScopeProtocol.Accepts([], [0, 0]));

    /// <summary>One margin for the shop: both cultures' dev counts are summed per margin before choosing.</summary>
    [Fact]
    public void The_cultures_are_summed_per_margin_in_grid_order()
    {
        var combined = QueryScopeProtocol.Combine(
        [
            new(0.05, 7, 10, 0),
            new(0.02, 9, 10, 1),
            new(0.05, 6, 10, 1),
            new(0.02, 8, 10, 1)
        ]);

        Assert.Equal([new MarginResult(0.02, 17, 20, 2), new MarginResult(0.05, 13, 20, 1)], combined);
    }

    [Fact]
    public void A_margin_one_culture_would_choose_is_refused_when_the_other_culture_marks_a_real_query_out() =>
        Assert.Equal(0.08, QueryScopeProtocol.Choose(QueryScopeProtocol.Combine(
        [
            new(0.05, 9, 10, 0), new(0.05, 9, 10, 1),
            new(0.08, 6, 10, 0), new(0.08, 6, 10, 0)
        ])));

    [Fact]
    public void The_grid_is_the_one_declared_before_measuring() =>
        Assert.Equal([0.02, 0.05, 0.08, 0.12], QueryScopeProtocol.Grid);
}
