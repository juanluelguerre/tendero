using System.Text.Json;
using ElGuerre.Tendero.Catalog.Features.GetProduct;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Tests.Ucp;
using ElGuerre.Tendero.Ucp.Catalog;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// The catalogue binding's answers, checked against the specification's own
/// response schemas.
///
/// The envelopes are composed by a pure method in `src/Ucp` precisely so this
/// can happen with no database, no server and no MCP client — and it earned that
/// immediately. Running the schemas over what had been written from a
/// documentation page found four things wrong at once: a `Category` is
/// `{ value, taxonomy }` and not `{ id, name }`; response pagination is
/// `{ has_next_page, cursor, total_count }` and not `{ limit, offset, total }`;
/// the envelope's `request_id` and `timestamp` exist nowhere in the protocol;
/// and `meta` is optional with `ucp-agent` NESTED, where this binding had been
/// requiring a flat `"ucp-agent.profile"` and refusing every conformant caller
/// that did not send it.
/// </summary>
public sealed class UcpCatalogConformanceTests
{
    private const string SearchResponse = "https://ucp.dev/schemas/shopping/catalog_search.json#/$defs/search_response";
    private const string LookupResponse = "https://ucp.dev/schemas/shopping/catalog_lookup.json#/$defs/lookup_response";
    private const string ProductResponse = "https://ucp.dev/schemas/shopping/catalog_lookup.json#/$defs/get_product_response";

    private const string BaseUrl = "https://shop.example";

    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static JsonElement Json<T>(T answer) =>
        JsonDocument.Parse(JsonSerializer.Serialize(answer, Wire)).RootElement.Clone();

    private static ProductDetail Product(string code) => new(
        ProductId: "0199a1f0-0000-7000-8000-000000000001",
        Code: code,
        Name: "Funda para portatil de 14 pulgadas",
        Slug: "funda-para-portatil",
        Description: "Funda acolchada de fieltro de lana.",
        Brand: "Nordwind",
        Category: [new CategoryStep("BAGS", "Bolsas")],
        Images: [new ProductImageView("abc123", "Funda negra", 0)],
        Attributes: [],
        Axes: [new VariantAxisView("COLOR", "color", [new VariantOptionView("BLACK", "negro")])],
        Variants:
        [
            new VariantView(
                "0199a1f0-0000-7000-8000-000000000002", $"{code}-BLACK", 39.00m, "EUR",
                new Dictionary<string, string> { ["COLOR"] = "BLACK" },
                "negro", "abc123", false, true, 4)
        ],
        PriceFrom: 39.00m,
        PriceTo: 39.00m,
        PriceCurrency: "EUR",
        Alternates: [],
        MissingCultures: [],
        Status: "Active",
        UpdatedAt: DateTimeOffset.UnixEpoch);

    [Fact]
    public void A_search_answer_validates() =>
        UcpSchemas.Validate(
            SearchResponse,
            Json(UcpCatalogAnswers.Search([Product("B13BAG0710")], BaseUrl, limit: 10, offset: 0, total: 1, notices: [])),
            "A search_catalog answer");

    /// <summary>
    /// Nothing found is a `messages` entry and a valid response, not an error.
    /// The specification draws that line and this is it made executable.
    /// </summary>
    [Fact]
    public void A_search_that_found_nothing_validates_too()
    {
        var answer = UcpCatalogAnswers.Search([], BaseUrl, limit: 10, offset: 0, total: 0, notices: []);

        UcpSchemas.Validate(SearchResponse, Json(answer), "An empty search_catalog answer");

        Assert.Equal("info", Assert.Single(answer.Messages!).Type);
        Assert.False(answer.Pagination!.HasNextPage);
        Assert.Null(answer.Pagination.Cursor);
    }

    /// <summary>
    /// A cursor MUST be present when there is a next page, and must not point at
    /// one that does not exist.
    /// </summary>
    [Fact]
    public void A_page_with_more_behind_it_carries_a_cursor()
    {
        var more = UcpCatalogAnswers.Search([Product("B13BAG0710")], BaseUrl, limit: 1, offset: 0, total: 5, notices: []);
        var last = UcpCatalogAnswers.Search([Product("B13BAG0710")], BaseUrl, limit: 1, offset: 4, total: 5, notices: []);

        UcpSchemas.Validate(SearchResponse, Json(more), "A search_catalog answer with a next page");

        Assert.True(more.Pagination!.HasNextPage);
        Assert.Equal("1", more.Pagination.Cursor);

        Assert.False(last.Pagination!.HasNextPage);
        Assert.Null(last.Pagination.Cursor);
    }

    /// <summary>
    /// The notices `UcpSearchNotices` builds from a <see cref="SearchAnswer"/> —
    /// an information message and a help-topic message together — validated
    /// against the same schema every other `messages` entry answers to. Nothing
    /// about them is special-cased in the schema, and this is what proves it.
    /// </summary>
    [Fact]
    public void A_search_with_notices_validates()
    {
        var notices = UcpSearchNotices.For(
            new SearchAnswer(SearchMatch.Related, HelpTopic: "returns"), "devoluciones", "es");

        Assert.Equal(2, notices.Count);

        UcpSchemas.Validate(
            SearchResponse,
            Json(UcpCatalogAnswers.Search([Product("B13BAG0710")], BaseUrl, limit: 10, offset: 0, total: 1, notices)),
            "A search_catalog answer carrying notices");
    }

    /// <summary>
    /// Where notices go, which the schema cannot say: an absent <c>messages</c>
    /// and an empty one are both valid, and so is a page whose notices were
    /// dropped. Added at the 2026-09-17 phase close, where each of those three
    /// mutations survived.
    /// </summary>
    [Fact]
    public void A_page_with_products_and_nothing_to_say_carries_no_messages() =>
        Assert.Null(UcpCatalogAnswers.Search([Product("B13BAG0710")], BaseUrl, limit: 10, offset: 0, total: 1, notices: []).Messages);

    [Fact]
    public void A_page_with_products_carries_its_notices_and_only_them()
    {
        var notices = UcpSearchNotices.For(new SearchAnswer(SearchMatch.Related), "regalo abuela", "es");

        var answer = UcpCatalogAnswers.Search([Product("B13BAG0710")], BaseUrl, limit: 10, offset: 0, total: 1, notices);

        Assert.Equal(["no_exact_match"], answer.Messages!.Select(message => message.Code));
    }

    /// <summary>
    /// A notice that is still true of an empty page travels with it, and
    /// "no_results" comes after it. Only "no_exact_match" is dropped — see the
    /// test below for why.
    /// </summary>
    [Fact]
    public void An_empty_page_says_so_after_its_notices()
    {
        var notices = UcpSearchNotices.For(new SearchAnswer(SearchMatch.Related, HelpTopic: "returns"), "devoluciones", "es");

        var answer = UcpCatalogAnswers.Search([], BaseUrl, limit: 10, offset: 0, total: 0, notices);

        Assert.Equal(["help_available", "no_results"], answer.Messages!.Select(message => message.Code));
        Assert.Equal("$.query", answer.Messages![^1].Path);
    }

    /// <summary>
    /// The defect this guards against: a `Related` answer's "here are the
    /// closest matches" notice survived onto a page with nothing on it — an
    /// offset past the end, or a query with no relevant products at all — and
    /// contradicted the "no_results" message right beside it. Found in review,
    /// 2026-09-17.
    /// </summary>
    [Fact]
    public void An_empty_related_page_says_only_no_results()
    {
        var notices = UcpSearchNotices.For(new SearchAnswer(SearchMatch.Related), "regalo abuela", "es");

        var answer = UcpCatalogAnswers.Search([], BaseUrl, limit: 10, offset: 0, total: 0, notices);

        UcpSchemas.Validate(SearchResponse, Json(answer), "An empty related search_catalog answer");

        Assert.Equal(["no_results"], answer.Messages!.Select(message => message.Code));
    }

    /// <summary>
    /// A help topic says something true whether or not the page is empty, so it
    /// is not the contradiction "no_exact_match" is, and it survives beside
    /// "no_results".
    /// </summary>
    [Fact]
    public void An_empty_related_page_with_a_help_topic_keeps_it_alongside_no_results()
    {
        var notices = UcpSearchNotices.For(
            new SearchAnswer(SearchMatch.Related, HelpTopic: "returns"), "devoluciones", "es");

        var answer = UcpCatalogAnswers.Search([], BaseUrl, limit: 10, offset: 0, total: 0, notices);

        UcpSchemas.Validate(SearchResponse, Json(answer), "An empty related search_catalog answer with a help topic");

        Assert.Equal(["help_available", "no_results"], answer.Messages!.Select(message => message.Code));
    }

    [Fact]
    public void A_lookup_answer_validates() =>
        UcpSchemas.Validate(
            LookupResponse,
            Json(UcpCatalogAnswers.Lookup([Product("B13BAG0710")], ["B13BAG0710"], BaseUrl)),
            "A lookup_catalog answer");

    /// <summary>
    /// And an id nobody owns is a warning inside a valid answer rather than a
    /// failed call — the whole lookup did not fail because one of five did.
    /// </summary>
    [Fact]
    public void A_lookup_with_an_unknown_id_validates_and_says_which()
    {
        var answer = UcpCatalogAnswers.Lookup([Product("B13BAG0710")], ["B13BAG0710", "ZZZZZZZZZZ"], BaseUrl);

        UcpSchemas.Validate(LookupResponse, Json(answer), "A lookup_catalog answer with a miss");

        var message = Assert.Single(answer.Messages!);
        Assert.Equal("warning", message.Type);
        Assert.Contains("ZZZZZZZZZZ", message.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_product_answer_validates() =>
        UcpSchemas.Validate(
            ProductResponse,
            Json(UcpCatalogAnswers.Product(Product("B13BAG0710"), BaseUrl)),
            "A get_product answer");

    /// <summary>
    /// The control. Each row is a response that must be REJECTED, and each is a
    /// shape this binding actually served at some point today.
    /// </summary>
    [Theory]
    [InlineData("no ucp envelope", """{"products":[]}""")]
    [InlineData("no products array", """{"ucp":{"version":"2026-04-08"}}""")]
    [InlineData(
        "the old limit/offset/total pagination, which is the request shape and not the response one",
        """{"ucp":{"version":"2026-04-08"},"products":[],"pagination":{"limit":10,"offset":0,"total":0}}""")]
    public void A_search_answer_that_breaks_the_schema_is_rejected(string what, string json) =>
        Assert.False(
            UcpSchemas.Get(SearchResponse).Evaluate(JsonDocument.Parse(json).RootElement).IsValid,
            $"A search response with {what} must not validate.");
}
