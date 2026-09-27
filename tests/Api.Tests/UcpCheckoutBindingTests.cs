using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// Checkout's bindings at the edge (P11-1), where no database is reached: who may
/// call them, and what happens before anything is dispatched — the token, the
/// `UCP-Agent` profile and whose host it is on. The flow itself is proven on
/// Postgres in `UcpCheckoutSessionTests`.
/// </summary>
public sealed class UcpCheckoutBindingTests
{
    private const string Sessions = "/ucp/checkout-sessions";

    private static readonly object Body = new { line_items = new[] { new { item = new { id = "B05DEFG606-DEFAULT" }, quantity = 1 } } };

    [Fact]
    public async Task Nobody_and_a_person_are_refused_before_anything_else()
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var anonymous = await client.PostAsJsonAsync(Sessions, Body, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var person = await PostAsync(client, await TokenAsync(client, "ana", ct), null, ct);
        Assert.Equal(HttpStatusCode.Forbidden, person.StatusCode);
    }

    [Fact]
    public async Task An_agent_without_a_profile_is_told_to_send_one()
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var response = await PostAsync(client, await TokenAsync(client, "claude-desktop-test", ct), null, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_profile_url", (await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct))!["code"]!.GetValue<string>());
    }

    /// <summary>
    /// The shop fetches no profile from a host it does not trust, however the agent
    /// asks: the answer comes back without a request being made to that host.
    /// </summary>
    [Fact]
    public async Task A_profile_on_an_untrusted_host_is_never_fetched()
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var response = await PostAsync(client, await TokenAsync(client, "claude-desktop-test", ct), "profile=\"https://evil.example/profile.json\"", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_profile_url", (await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct))!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Over_mcp_the_checkout_tools_need_an_agents_token_and_the_catalogue_does_not()
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new { name = "get_checkout", arguments = new { id = Guid.NewGuid().ToString(), meta = new Dictionary<string, object> { ["ucp-agent"] = new { profile = "https://agent.example/p" } } } },
            }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        var response = await client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        Assert.True(text.Contains("bearer token", StringComparison.Ordinal), text);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string token, string? agentHeader, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Sessions) { Content = JsonContent.Create(Body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (agentHeader is not null)
            request.Headers.TryAddWithoutValidation("UCP-Agent", agentHeader);
        return await client.SendAsync(request, ct);
    }

    private static async Task<string> TokenAsync(HttpClient client, string subject, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent([new("grant_type", "password"), new("username", subject)]);
        var response = await client.PostAsync("/dev-issuer/connect/token", form, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!["access_token"]!.GetValue<string>();
    }
}
