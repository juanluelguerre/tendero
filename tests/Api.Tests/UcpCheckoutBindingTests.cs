using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// **No token is HTTP's 401, with where to get one** (MCP, Authorization;
    /// RFC 9728; RFC 6750). It was a JSON-RPC error over a 200, which a client
    /// reads as a call that failed — and a client that would have signed in
    /// never learns it could. The challenge names the resource's metadata, and
    /// the metadata names the issuer. A person's token is a 403: signed in, and
    /// not what a checkout asks for.
    /// </summary>
    [Fact]
    public async Task Over_mcp_a_checkout_tool_with_no_token_is_a_401_that_says_where_to_get_one()
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        using var anonymous = await McpAsync(client, null, "get_checkout", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var challenge = Assert.Single(anonymous.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"", challenge.Parameter, StringComparison.Ordinal);

        var metadata = JsonNode.Parse(await client.GetStringAsync("/.well-known/oauth-protected-resource/mcp", ct))!;
        Assert.Equal("http://localhost/mcp", metadata["resource"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(metadata["authorization_servers"]!.AsArray())!.GetValue<string>()));
        Assert.Equal("header", Assert.Single(metadata["bearer_methods_supported"]!.AsArray())!.GetValue<string>());

        // Somebody signed in is looked up in the directories, and there is no
        // database here: nobody is registered.
        using var known = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<ICustomerDirectory, NobodyRegistered>();
            services.AddScoped<IAgentDirectory, NobodyRegistered>();
        }));
        using var signedIn = known.CreateClient();

        using var person = await McpAsync(signedIn, await TokenAsync(client, "ana", ct), "get_checkout", ct);
        Assert.Equal(HttpStatusCode.Forbidden, person.StatusCode);
    }

    /// <summary>
    /// The door is on five tools, not on the endpoint: what reads the public
    /// catalogue stays open to anyone (ADR 0039), and so does asking what the
    /// server offers.
    /// </summary>
    [Theory]
    [InlineData("tools/list", null)]
    [InlineData("tools/call", "get_return_policy")]
    public async Task Over_mcp_what_only_reads_the_shop_asks_for_no_token(string method, string? tool)
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        using var response = await McpAsync(client, null, tool, TestContext.Current.CancellationToken, method);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// *Invalid or expired tokens MUST receive a HTTP 401* (MCP, Authorization):
    /// on an endpoint that is open, a token nobody signed was simply ignored,
    /// and its bearer was served as nobody without being told.
    /// </summary>
    [Fact]
    public async Task Over_mcp_a_token_that_is_not_good_is_a_401_even_where_none_is_needed()
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        using var response = await McpAsync(client, "not-a-token", null, TestContext.Current.CancellationToken, "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("error=\"invalid_token\"", Assert.Single(response.Headers.WwwAuthenticate).Parameter, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> McpAsync(HttpClient client, string? token, string? tool, CancellationToken ct, string method = "tools/call")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method,
                @params = tool is null ? (object)new { } : new { name = tool, arguments = new { id = Guid.NewGuid().ToString(), meta = new Dictionary<string, object> { ["ucp-agent"] = new { profile = "https://agent.example/p" } } } },
            }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request, ct);
    }

    /// <summary>
    /// **A protocol error is a JSON-RPC error, with the specification's code**
    /// (UCP §Error Codes): `-32001` for discovery — the profile missing, on a
    /// host nobody trusts, unreachable, malformed, at another version — and
    /// `-32000` for the rest, with UCP's own `code` and `content` in
    /// `error.data`. They were tool results marked `isError`, which is where a
    /// business outcome goes: a platform that branches on `error.code`, as the
    /// specification tells it to, saw a call that had succeeded.
    /// </summary>
    [Fact]
    public async Task Over_mcp_a_protocol_error_is_a_json_rpc_error_with_ucps_code()
    {
        // Over MCP the tool asks who is calling, and that reads the two
        // directories; there is no database here, so nobody is registered.
        using var root = new TenderoApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<ICustomerDirectory, NobodyRegistered>();
            services.AddScoped<IAgentDirectory, NobodyRegistered>();
        }));
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        // Signed by the host whose keys the validator reads: the derived host
        // validates against the root's, which is where the factory points it.
        using var issuer = root.CreateClient();
        var agent = await TokenAsync(issuer, "claude-desktop-test", ct);

        // Nobody at all never reaches a tool: that is HTTP's 401, above.
        var noProfile = await CallAsync(client, agent, null, ct);
        var untrusted = await CallAsync(client, agent, new { profile = "https://evil.example/profile.json" }, ct);

        Assert.Equal((-32001, "invalid_profile_url"), (noProfile["error"]!["code"]!.GetValue<int>(), noProfile["error"]!["data"]!["code"]!.GetValue<string>()));
        Assert.Equal((-32001, "invalid_profile_url"), (untrusted["error"]!["code"]!.GetValue<int>(), untrusted["error"]!["data"]!["code"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(untrusted["error"]!["data"]!["content"]!.GetValue<string>()));
        Assert.Null(untrusted["result"]);
    }

    /// <summary>One `get_checkout` over MCP, as whoever the token says; the answer whichever way the transport framed it.</summary>
    private static async Task<JsonNode> CallAsync(HttpClient client, string? token, object? agent, CancellationToken ct)
    {
        var meta = new Dictionary<string, object>();
        if (agent is not null)
            meta["ucp-agent"] = agent;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new { name = "get_checkout", arguments = new { id = Guid.NewGuid().ToString(), meta } },
            }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var text = await (await client.SendAsync(request, ct)).Content.ReadAsStringAsync(ct);
        var json = text.Split('\n').FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal)) is { } data ? data[5..] : text;
        return JsonNode.Parse(json)!;
    }

    private sealed class NobodyRegistered : ICustomerDirectory, IAgentDirectory
    {
        public Task<KnownCustomer?> ForSubjectAsync(string subject, CancellationToken ct = default) => Task.FromResult<KnownCustomer?>(null);

        public Task<AgentStanding?> StandingOfAsync(AgentId id, CancellationToken ct = default) => Task.FromResult<AgentStanding?>(null);
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
