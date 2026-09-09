using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ElGuerre.Tendero.Tests.Ucp;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// UCP's REST binding, over real HTTP, with its answers validated against the
/// specification's response schemas.
///
/// The operations that need no catalogue are the ones asserted here — an empty
/// lookup, the refusals — because the payload shapes are already proven against
/// every shipped product in the integration suite. What only this level can show
/// is the HTTP half: the paths, the status codes, and the rule that makes REST
/// bindings surprising.
///
/// **200 for a lookup that found nothing.** That is the specification's choice
/// and it goes against the reflex: an identifier nobody owns is a business
/// outcome, so it comes back as a 200 with a `messages` entry naming it. A 404
/// would tell a caller their request was malformed when it was perfectly well
/// formed. `get_product` is the exception, and by construction: its response
/// schema requires a `product`, so there is no valid 200 shape for a miss.
/// </summary>
public sealed class UcpRestBindingTests
{
    private const string LookupResponse = "https://ucp.dev/schemas/shopping/catalog_lookup.json#/$defs/lookup_response";

    [Fact]
    public async Task A_lookup_answers_200_and_a_conformant_body()
    {
        var ct = TestContext.Current.CancellationToken;

        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/ucp/catalog/lookup", new { ids = Array.Empty<string>() }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;

        UcpSchemas.Validate(LookupResponse, body, "A REST lookup answer");
        Assert.Equal("2026-04-08", body.GetProperty("ucp").GetProperty("version").GetString());
    }

    /// <summary>
    /// The binding's paths are the specification's. A shop that served them
    /// somewhere else would still be conformant about its payloads and
    /// unreachable to anybody following the spec, which is the worst of both.
    /// </summary>
    [Theory]
    [InlineData("/ucp/catalog/search")]
    [InlineData("/ucp/catalog/lookup")]
    [InlineData("/ucp/catalog/product")]
    public async Task The_specifications_paths_are_the_ones_served(string path)
    {
        var ct = TestContext.Current.CancellationToken;

        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(path, new { ids = Array.Empty<string>(), id = "ZZZZZZZZZZ" }, ct);

        // Anything but "there is no such route". What each one answers is
        // asserted elsewhere; this is about the route existing.
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    /// <summary>
    /// Malformed agent metadata is a 400 — the caller's mistake, and the same
    /// sentence the MCP binding gives, because both ask the same function.
    /// </summary>
    [Fact]
    public async Task Malformed_agent_metadata_is_a_400_naming_the_member()
    {
        var ct = TestContext.Current.CancellationToken;

        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/ucp/catalog/lookup", new
        {
            ids = Array.Empty<string>(),
            meta = new Dictionary<string, object> { ["ucp-agent"] = new { } }
        }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("profile", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    /// <summary>
    /// A batch beyond the limit is a 400 too, and it says the number. The
    /// specification names request-too-large as a 400 rather than a business
    /// outcome, which is the line: the request itself is wrong.
    /// </summary>
    [Fact]
    public async Task A_batch_beyond_the_limit_is_a_400_with_the_number()
    {
        var ct = TestContext.Current.CancellationToken;

        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/ucp/catalog/lookup", new
        {
            ids = Enumerable.Range(0, 60).Select(n => $"ID{n:0000000}").ToArray()
        }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("60", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_product_request_with_no_id_is_a_400()
    {
        var ct = TestContext.Current.CancellationToken;

        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/ucp/catalog/product", new { }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// And it is public, because the profile that advertises it is: discovery
    /// that leads to a door needing a credential nobody was told about is not
    /// discovery.
    /// </summary>
    [Fact]
    public async Task It_is_reachable_without_a_token()
    {
        var ct = TestContext.Current.CancellationToken;

        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/ucp/catalog/lookup", new { ids = Array.Empty<string>() }, ct);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
