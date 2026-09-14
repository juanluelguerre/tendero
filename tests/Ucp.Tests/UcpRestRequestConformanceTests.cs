using System.Text.Json;
using ElGuerre.Tendero.Tests.Ucp;
using ElGuerre.Tendero.Ucp.Catalog;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// The REST binding is the only one where **both directions** have a schema, so
/// it is the only one where a request can be checked as well as an answer.
///
/// Over MCP the arguments are tool parameters and only the response is
/// specified; over REST the body IS
/// <c>catalog_search.json#/$defs/search_request</c>. That makes these tests
/// worth more than they look: they assert that what a conformant agent sends
/// **binds** to our records without losing anything, which is the failure a
/// response test cannot see.
/// </summary>
public sealed class UcpRestRequestConformanceTests
{
    private const string SearchRequest = "https://ucp.dev/schemas/shopping/catalog_search.json#/$defs/search_request";
    private const string LookupRequest = "https://ucp.dev/schemas/shopping/catalog_lookup.json#/$defs/lookup_request";
    private const string ProductRequest = "https://ucp.dev/schemas/shopping/catalog_lookup.json#/$defs/get_product_request";

    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static JsonElement Json<T>(T value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value, Wire)).RootElement.Clone();

    /// <summary>
    /// What this shop would send if it were the agent: everything it supports,
    /// serialised, validated. If a field name is wrong here it is wrong on the
    /// way in too, because the same record does the binding.
    /// </summary>
    [Fact]
    public void A_full_search_request_validates() =>
        UcpSchemas.Validate(SearchRequest, Json(new UcpSearchRequest
        {
            Query = "mochila impermeable",
            Filters = new Dictionary<string, string> { ["category"] = "BAGS" },
            Pagination = new UcpPageRequest { Limit = 10, Cursor = "20" },
            Meta = new Dictionary<string, JsonElement>
            {
                ["ucp-agent"] = JsonDocument.Parse("""{"profile":"https://agent.example/p"}""").RootElement
            }
        }), "A full search request");

    /// <summary>
    /// And an empty one, because the schema requires nothing of a search: no
    /// arguments at all is a browse, and a binding that demanded a query would
    /// be stricter than the protocol — the mistake this project already made
    /// once with `meta`.
    /// </summary>
    [Fact]
    public void An_empty_search_request_validates() =>
        UcpSchemas.Validate(SearchRequest, Json(new UcpSearchRequest()), "An empty search request");

    [Fact]
    public void A_lookup_request_validates() =>
        UcpSchemas.Validate(LookupRequest, Json(new UcpLookupRequest
        {
            Ids = ["B13BAG0710", "B10SHO0101"]
        }), "A lookup request");

    [Fact]
    public void A_get_product_request_validates() =>
        UcpSchemas.Validate(ProductRequest, Json(new UcpGetProductRequest
        {
            Id = "B13BAG0710"
        }), "A get_product request");

    /// <summary>
    /// The controls. A lookup with no <c>ids</c> and a product request with no
    /// <c>id</c> are the two the schema actually requires, and the binding
    /// refuses both with a 400 — so the schema and the endpoint agree about what
    /// a malformed request is.
    /// </summary>
    [Theory]
    [InlineData(LookupRequest, """{"context":{}}""")]
    [InlineData(ProductRequest, """{"context":{}}""")]
    public void A_request_missing_what_the_schema_requires_is_rejected(string schema, string json) =>
        Assert.False(
            UcpSchemas.Get(schema).Evaluate(JsonDocument.Parse(json).RootElement).IsValid,
            "A request missing a required member must not validate.");
}
