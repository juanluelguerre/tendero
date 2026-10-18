using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// Two things the MCP specification says a server MUST do that this one did not
/// (found on 2026-10-13 by an audit against the specification itself, phase 13b):
/// *Servers MUST validate the Origin header on all incoming connections*, and
/// *Servers MUST rate limit tool invocations*. Neither had a test, because the
/// transport tests were written from the SDK's behaviour and the SDK does not
/// do either for you.
///
/// Each test starts its own host, unlike the classes that share one: the rate
/// limit is the host's state, and a shared host let one test spend the next
/// one's allowance (four failures, 2026-10-18).
/// </summary>
public sealed class McpServerGuardsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly object ACall = new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "tools/call",
        @params = new { name = "get_return_policy", arguments = new { } },
    };

    private static HttpRequestMessage Call(string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(ACall) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (origin is not null)
            request.Headers.Add("Origin", origin);
        return request;
    }

    /// <summary>
    /// A page on somebody else's site, open in a shopper's browser, can post to
    /// a server the browser can reach — DNS rebinding is that attack — and
    /// `Origin` is how the server tells. *If the Origin header is present and
    /// invalid, servers MUST respond with HTTP 403.* CORS does not do it: CORS
    /// stops the page reading the answer, not the request being served.
    /// </summary>
    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:9999")]
    [InlineData("null")]
    public async Task A_request_from_an_origin_the_shop_does_not_know_is_refused(string origin)
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Call(origin), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// An agent that is not a browser sends no `Origin`, and is served: the rule
    /// is about a header that is present. So are the shop's own two front ends,
    /// and whoever asks from the address the server itself answers at.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:4200")]
    [InlineData("http://localhost:4201")]
    [InlineData("http://localhost")]
    public async Task A_request_with_no_origin_or_from_the_shops_own_is_served(string? origin)
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Call(origin), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Anybody may call, so what bounds a caller is how much: a hundred and
    /// twenty calls a minute an address — an agent's whole conversation and more
    /// — and then a 429, not a queue.
    /// </summary>
    [Fact]
    public async Task Tool_calls_are_limited_per_client()
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        for (var call = 0; call < 120; call++)
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Call(), Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Call(), Ct)).StatusCode);
    }
}
