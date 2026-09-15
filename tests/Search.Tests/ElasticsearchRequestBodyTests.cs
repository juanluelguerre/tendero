using System.Text;
using Elastic.Clients.Elasticsearch;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Elasticsearch;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// What the Elastic client actually sends.
///
/// A second <c>.Filter(...)</c> on a bool query, or a second <c>.Term(...)</c> on one
/// query descriptor, REPLACES the first: it compiles and says nothing. On
/// 2026-09-15 that had the card reader dropping its id filter — hybrid search kept
/// whichever products meaning found by luck — and a category browse dropping
/// <c>status: active</c>. The request body is the only place the mistake shows, so
/// the body is what these tests read. Nothing listens on the node: the call fails
/// after the body is written, and only the body is asserted.
/// </summary>
public sealed class ElasticsearchRequestBodyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_card_reader_asks_for_those_products_active_and_inside_the_branch()
    {
        var (client, bodies) = Capturing();

        await Assert.ThrowsAnyAsync<Exception>(() => new ElasticsearchHitReader(client).FindAsync(["p1", "p2"], "es", "KITCHEN", Ct));

        var body = Assert.Single(bodies);
        Assert.Contains("\"productId\"", body);
        Assert.Contains("\"p2\"", body);
        Assert.Contains("\"status\"", body);
        Assert.Contains("\"categoryCodes\"", body);
    }

    [Fact]
    public async Task A_category_browse_still_asks_for_active_products_only()
    {
        var (client, bodies) = Capturing();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new ElasticsearchLexicalSearch(client).SearchAsync(new ProductSearchQuery("", "es", Category: "KITCHEN"), Ct));

        var body = Assert.Single(bodies);
        Assert.Contains("\"status\"", body);
        Assert.Contains("\"categoryCodes\"", body);
    }

    [Fact]
    public async Task The_full_match_count_filters_like_the_search_it_stands_for()
    {
        var (client, bodies) = Capturing();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new ElasticsearchFullMatch(client).AnyAsync(new ProductSearchQuery("poner la mesa", "es", Category: "KITCHEN"), Ct));

        var body = Assert.Single(bodies);
        Assert.Contains("\"status\"", body);
        Assert.Contains("\"categoryCodes\"", body);
        Assert.Contains("\"cross_fields\"", body);
        Assert.Contains("\"fuzziness\"", body);
    }

    [Fact]
    public async Task A_page_starts_after_the_pages_before_it()
    {
        var body = await LexicalBody(new ProductSearchQuery("cafetera", "es", Page: 3, PageSize: 10));

        Assert.Contains("\"from\":20", body);
        Assert.Contains("\"size\":10", body);
    }

    [Fact]
    public async Task Newest_first_sorts_by_availability_and_relevance_does_not()
    {
        Assert.Contains("\"availableFrom\"", await LexicalBody(new ProductSearchQuery("", "es", Sort: "newest")));
        Assert.DoesNotContain("\"availableFrom\"", await LexicalBody(new ProductSearchQuery("cafetera", "es")));
    }

    [Fact]
    public async Task No_words_is_a_browse_and_words_are_a_search()
    {
        var browse = await LexicalBody(new ProductSearchQuery("", "es", Category: "KITCHEN"));
        var search = await LexicalBody(new ProductSearchQuery("cafetera", "es"));

        Assert.Contains("\"match_all\"", browse);
        Assert.DoesNotContain("\"multi_match\"", browse);
        Assert.Contains("\"multi_match\"", search);
        Assert.DoesNotContain("\"match_all\"", search);
    }

    [Fact]
    public async Task Without_a_category_no_branch_filter_is_sent()
    {
        var body = await LexicalBody(new ProductSearchQuery("cafetera", "es"));

        Assert.Contains("\"status\"", body);
        Assert.DoesNotContain("\"categoryCodes\"", body);
    }

    private static async Task<string> LexicalBody(ProductSearchQuery query)
    {
        var (client, bodies) = Capturing();

        await Assert.ThrowsAnyAsync<Exception>(() => new ElasticsearchLexicalSearch(client).SearchAsync(query, Ct));

        return Assert.Single(bodies);
    }

    private static (ElasticsearchClient Client, List<string> Bodies) Capturing()
    {
        var bodies = new List<string>();

        var settings = new ElasticsearchClientSettings(new Uri("http://127.0.0.1:9"))
            .DisableDirectStreaming()
            .OnRequestCompleted(call =>
            {
                if (call.RequestBodyInBytes is { Length: > 0 } body)
                    bodies.Add(Encoding.UTF8.GetString(body));
            });

        return (new ElasticsearchClient(settings), bodies);
    }
}
