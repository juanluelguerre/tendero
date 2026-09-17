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
        Assert.Contains("\"active\"", body);
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
        Assert.Contains("\"active\"", body);
        Assert.Contains("\"categoryCodes\"", body);
    }

    /// <summary>
    /// The notice's question is "does the shop sell this", so it reads what a product IS —
    /// name, brand, attributes, category — and never its description, and forgives no
    /// typos. At a million products some description contains the words of almost any
    /// query, and a fuzzy match turns "movil" into "mobil"; either would make the notice
    /// vanish exactly where it is needed (spec D5).
    ///
    /// **One clause per WORD, not one clause for the whole query (fix round 1).**
    /// `brand` carries no analyser (it keeps a `raw` keyword subfield instead), while
    /// `name`, `attributesText` and `categoryPathText` use the culture analyser —
    /// and `cross_fields` only blends fields that share one. A single cross_fields/AND
    /// clause over the whole text silently split into two groups, `{name,
    /// attributesText, categoryPathText}` and `{brand}`, so "Kuisina cafetera" needed
    /// "Kuisina" AND "cafetera" from a group with no brand field — impossible. Three
    /// words means three `best_fields` clauses here, each across every identity field.
    /// `zero_terms_query: all` is what lets "la" — a stopword the Spanish analyser
    /// empties — pass its own clause instead of making the whole AND impossible.
    /// </summary>
    [Fact]
    public async Task The_full_match_count_reads_what_a_product_is_and_forgives_nothing()
    {
        var (client, bodies) = Capturing();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new ElasticsearchFullMatch(client).AnyAsync(new ProductSearchQuery("poner la mesa", "es", Category: "KITCHEN"), Ct));

        var body = Assert.Single(bodies);
        Assert.Contains("\"status\"", body);
        Assert.Contains("\"active\"", body);
        Assert.Contains("\"categoryCodes\"", body);
        Assert.Equal(3, Occurrences(body, "\"multi_match\""));
        Assert.Equal(3, Occurrences(body, "\"best_fields\""));
        Assert.Equal(3, Occurrences(body, "\"zero_terms_query\":\"all\""));
        Assert.Contains("\"operator\":\"and\"", body);
        Assert.Contains("\"name\"", body);
        Assert.Contains("\"categoryPathText\"", body);
        Assert.DoesNotContain("\"cross_fields\"", body);
        Assert.DoesNotContain("\"fuzziness\"", body);
        Assert.DoesNotContain("description", body);
    }

    /// <summary>
    /// The catalogue writes "6 litros" and "24 cm"; shoppers write "6l" and "24cm".
    /// A bare measurement is one token here and two there, so it can never full-match
    /// and the notice fired above the right pan. The full-match check skips it
    /// (ADR 0032, fix round 2) — the question is whether the shop carries this KIND of
    /// thing, not whether it carries that size.
    /// </summary>
    [Theory]
    [InlineData("olla 6l", "es", 1)]
    [InlineData("sartén 24cm", "es", 1)]
    [InlineData("laptop backpack 15 inch", "en", 3)]
    [InlineData("olla 1,5l", "es", 1)]
    [InlineData("camiseta xl", "es", 2)]
    public async Task The_full_match_count_skips_a_bare_measurement(string text, string culture, int clauses)
    {
        var (client, bodies) = Capturing();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new ElasticsearchFullMatch(client).AnyAsync(new ProductSearchQuery(text, culture), Ct));

        var body = Assert.Single(bodies);
        Assert.Equal(clauses, Occurrences(body, "\"multi_match\""));
        Assert.Contains("\"status\"", body);
        Assert.Contains("\"active\"", body);
        Assert.DoesNotContain("\"fuzziness\"", body);
        Assert.DoesNotContain("description", body);
    }

    /// <summary>
    /// A query that is nothing but a measurement still has to ask something: skipping
    /// every word would leave an empty <c>must</c>, which matches everything and would
    /// silence the notice on every such query. When every word would be skipped, none
    /// is.
    /// </summary>
    [Theory]
    [InlineData("24cm", 1)]
    [InlineData("6l 24cm", 2)]
    public async Task A_query_that_is_only_a_measurement_still_asks_for_it(string text, int clauses)
    {
        var (client, bodies) = Capturing();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new ElasticsearchFullMatch(client).AnyAsync(new ProductSearchQuery(text, "es"), Ct));

        var body = Assert.Single(bodies);
        Assert.Equal(clauses, Occurrences(body, "\"multi_match\""));
        Assert.Equal(clauses, Occurrences(body, "\"zero_terms_query\":\"all\""));
    }

    /// <summary>
    /// Ranking is untouched: the search itself still asks for every word the shopper
    /// typed, measurement included, because dropping it there would rank a 24 cm pan
    /// level with a 28 cm one.
    /// </summary>
    [Fact]
    public async Task The_search_itself_still_carries_the_measurement()
    {
        var body = await LexicalBody(new ProductSearchQuery("sartén 24cm", "es"));

        Assert.Contains("24cm", body);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
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
        Assert.Contains("\"active\"", body);
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
