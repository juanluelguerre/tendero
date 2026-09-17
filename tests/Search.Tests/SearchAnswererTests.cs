using ElGuerre.Tendero.Search.Answers;
using ElGuerre.Tendero.Search.Contracts;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// Layer A: whether a page is an exact answer or the closest things the shop sells.
/// No model, so every rule here is a plain fact about the query and the index.
/// </summary>
public sealed class SearchAnswererTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_query_some_product_fully_matches_is_an_exact_answer()
    {
        var answer = await new SearchAnswerer(new FullMatch(true), new NoQueryScope()).AnswerAsync(new ProductSearchQuery("cafetera italiana", "es"), Ct);

        Assert.Equal(new SearchAnswer(SearchMatch.Exact), answer);
    }

    [Fact]
    public async Task A_query_no_product_fully_matches_is_answered_with_the_closest_things()
    {
        var answer = await new SearchAnswerer(new FullMatch(false), new NoQueryScope()).AnswerAsync(new ProductSearchQuery("regalo abuela", "es"), Ct);

        Assert.Equal(new SearchAnswer(SearchMatch.Related), answer);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("cafetera", "newest")]
    public async Task A_browse_or_a_date_order_has_no_answer_to_judge(string text, string? sort)
    {
        var fullMatch = new FullMatch(true);

        var answer = await new SearchAnswerer(fullMatch, new NoQueryScope()).AnswerAsync(new ProductSearchQuery(text, "es", Sort: sort), Ct);

        Assert.Null(answer);
        Assert.Equal(0, fullMatch.Asked);
    }

    [Fact]
    public async Task A_question_about_returns_carries_the_help_topic_beside_the_match()
    {
        var answer = await new SearchAnswerer(new FullMatch(false), new NoQueryScope()).AnswerAsync(new ProductSearchQuery("devoluciones", "es"), Ct);

        Assert.Equal(new SearchAnswer(SearchMatch.Related, HelpTopics.Returns), answer);
    }

    [Fact]
    public async Task A_query_out_of_scope_is_answered_as_not_sold_before_the_full_match_is_counted()
    {
        var fullMatch = new FullMatch(true);
        var scope = new FixedScope(new ScopeVerdict(ScopeOutcome.OutOfScope, "teléfonos móviles"));

        var answer = await new SearchAnswerer(fullMatch, scope).AnswerAsync(new ProductSearchQuery("móvil para mi madre", "es"), Ct);

        Assert.Equal(new SearchAnswer(SearchMatch.OutOfScope, OutOfScopeLabel: "teléfonos móviles"), answer);
        Assert.Equal(0, fullMatch.Asked);
    }

    [Theory]
    [InlineData(ScopeOutcome.InScope)]
    [InlineData(ScopeOutcome.Ambiguous)]
    [InlineData(ScopeOutcome.Unknown)]
    public async Task Any_other_verdict_leaves_layer_A_to_decide(ScopeOutcome outcome)
    {
        var answer = await new SearchAnswerer(new FullMatch(false), new FixedScope(new ScopeVerdict(outcome)))
            .AnswerAsync(new ProductSearchQuery("regalo abuela", "es"), Ct);

        Assert.Equal(new SearchAnswer(SearchMatch.Related), answer);
    }

    private sealed class FixedScope(ScopeVerdict verdict) : IQueryScope
    {
        public Task<ScopeVerdict> ClassifyAsync(string text, string culture, CancellationToken ct = default) =>
            Task.FromResult(verdict);
    }

    private sealed class FullMatch(bool any) : ILexicalFullMatch
    {
        public int Asked { get; private set; }

        public Task<bool> AnyAsync(ProductSearchQuery query, CancellationToken ct = default)
        {
            Asked++;
            return Task.FromResult(any);
        }
    }
}
