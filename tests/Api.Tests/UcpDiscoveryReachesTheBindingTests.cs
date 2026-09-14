using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The link between the two protocols, which is the one thing neither of their
/// own test suites can see.
///
/// **MCP and UCP are not alternatives.** MCP is a way to hand tools to a model —
/// JSON-RPC, `initialize`, `tools/list`. UCP is a commerce protocol that defines
/// capabilities and payloads and then names its transport bindings, and `mcp` is
/// one of them. So "UCP over MCP" is not a compromise: it is a combination the
/// specification defines, and this shop serves it.
///
/// What that leaves is a seam nothing else checks. The profile at
/// <c>/.well-known/ucp</c> says *call `dev.ucp.shopping` over mcp, at this
/// endpoint*. If that endpoint does not exist, or exists and does not offer the
/// binding's tools, the manifest is lying — and every test on either side stays
/// green, because each half is correct about itself.
///
/// So: read the profile, follow it, and check what is there.
/// </summary>
public sealed class UcpDiscoveryReachesTheBindingTests
{
    /// <summary>The three tool names the catalogue's MCP binding fixes.</summary>
    private static readonly string[] BindingTools = ["get_product", "lookup_catalog", "search_catalog"];

    /// <summary>
    /// Both bindings, walked from the profile that advertises them. The service
    /// is a LIST for exactly this reason: one capability, two ways in, and each
    /// one has to actually be there.
    /// </summary>
    [Fact]
    public async Task Every_binding_the_profile_advertises_is_really_served()
    {
        var ct = TestContext.Current.CancellationToken;

        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var profile = JsonDocument.Parse(
            await client.GetStringAsync("/.well-known/ucp", ct)).RootElement;

        var bindings = profile
            .GetProperty("ucp").GetProperty("services")
            .GetProperty("dev.ucp.shopping")
            .EnumerateArray()
            .Select(binding => (
                Transport: binding.GetProperty("transport").GetString()!,
                Endpoint: new Uri(binding.GetProperty("endpoint").GetString()!)))
            .ToArray();

        Assert.Equal(["mcp", "rest"], bindings.Select(b => b.Transport).Order(StringComparer.Ordinal));

        var rest = bindings.Single(binding => binding.Transport == "rest");

        // The REST binding's own paths hang off the endpoint the profile names.
        var response = await client.PostAsJsonAsync(
            $"{rest.Endpoint.AbsolutePath}/catalog/lookup",
            new { ids = Array.Empty<string>() },
            ct);

        response.EnsureSuccessStatusCode();

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal("2026-04-08", body.GetProperty("ucp").GetProperty("version").GetString());
    }

    [Fact]
    public async Task The_endpoint_the_profile_advertises_serves_the_binding_it_claims()
    {
        var ct = TestContext.Current.CancellationToken;

        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        // 1 — discovery. No MCP involved: it is an ordinary public GET.
        var profile = JsonDocument.Parse(
            await client.GetStringAsync("/.well-known/ucp", ct)).RootElement;

        var binding = profile
            .GetProperty("ucp").GetProperty("services")
            .GetProperty("dev.ucp.shopping")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("transport").GetString() == "mcp");

        // 2 — follow it. The profile hands out an absolute URL built from the
        // request's own host; the test server answers on the path.
        var endpoint = new Uri(binding.GetProperty("endpoint").GetString()!);

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await client.PostAsJsonAsync(
            endpoint.AbsolutePath,
            new { jsonrpc = "2.0", id = 1, method = "tools/list" },
            ct);

        response.EnsureSuccessStatusCode();

        // 3 — and the binding's tools are actually there.
        var body = await response.Content.ReadAsStringAsync(ct);
        var payload = body
            .Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim())
            .ToArray();

        var tools = JsonDocument
            .Parse(payload.Length > 0 ? string.Join(string.Empty, payload) : body)
            .RootElement
            .GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToArray();

        Assert.All(BindingTools, tool =>
            Assert.Contains(tool, tools));
    }

    /// <summary>
    /// And the two contracts stay apart on the one endpoint they share.
    ///
    /// A UCP answer is an envelope in <c>structuredContent</c>; a Tendero answer
    /// is the shop's own shape in <c>content</c>. If those ever converge, one of
    /// the two protocols is being served badly — and the tool list is the place
    /// it would show first, because that is what a client branches on.
    /// </summary>
    [Fact]
    public async Task The_two_contracts_are_distinguishable_on_the_endpoint_they_share()
    {
        var ct = TestContext.Current.CancellationToken;

        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var ucp = await CallAsync(client, "lookup_catalog", new { ids = Array.Empty<string>() }, ct);
        var shop = await CallAsync(client, "get_return_policy", new { }, ct);

        // The UCP one carries the protocol envelope where the binding says it must.
        Assert.True(ucp.TryGetProperty("structuredContent", out var structured));
        Assert.Equal("2026-04-08", structured.GetProperty("ucp").GetProperty("version").GetString());

        // The shop's own carries the shop's own answer, and no UCP envelope —
        // it is not pretending to be UCP, which is the honest half.
        Assert.False(shop.TryGetProperty("structuredContent", out _));
        Assert.Contains(
            "windowDays",
            shop.GetProperty("content")[0].GetProperty("text").GetString(),
            StringComparison.Ordinal);
    }

    private static async Task<JsonElement> CallAsync(
        HttpClient client, string tool, object arguments, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = tool, arguments }
        }, ct);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var payload = body
            .Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim())
            .ToArray();

        return JsonDocument
            .Parse(payload.Length > 0 ? string.Join(string.Empty, payload) : body)
            .RootElement.GetProperty("result").Clone();
    }
}
