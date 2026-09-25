using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// What an agent's call is counted as, read off the counter itself
/// (`tendero.agent.tool_calls`, the tracking plan) — and who the API says acted.
///
/// A metric is a statement no response carries, so the only way to know the
/// labels are right is to listen. The counters are static and shared by every
/// test in the process, so these run in a collection of their own, never beside
/// another test calling the same tools.
/// </summary>
[Collection(MetricsCollection.Name)]
public sealed class AgentToolCallMetricsTests
{
    [Fact]
    public async Task An_mcp_tool_that_answers_is_counted_as_answered()
    {
        using var listener = new InstrumentRecorder("tendero.agent.tool_calls");
        using var factory = new TenderoApiFactory();

        await CallMcp(factory, "get_return_policy", new { });

        Assert.Contains(Calls(listener), tags => tags is { surface: "mcp", tool: "get_return_policy", outcome: "answered" });
    }

    [Fact]
    public async Task An_mcp_refusal_is_counted_as_refused()
    {
        using var listener = new InstrumentRecorder("tendero.agent.tool_calls");
        using var factory = new TenderoApiFactory();

        await CallMcp(factory, "search_products", new { query = "z" });

        Assert.Contains(Calls(listener), tags => tags is { surface: "mcp", tool: "search_products", outcome: "refused" });
    }

    /// <summary>
    /// A tool that breaks is not a tool that said no. Elasticsearch is where
    /// nothing listens here, so a real search cannot be answered.
    /// </summary>
    [Fact]
    public async Task An_mcp_tool_that_breaks_is_counted_as_failed()
    {
        using var listener = new InstrumentRecorder("tendero.agent.tool_calls");
        using var factory = new TenderoApiFactory();

        await CallMcp(factory, "search_products", new { query = "zapatillas" });

        Assert.Contains(Calls(listener), tags => tags is { surface: "mcp", tool: "search_products", outcome: "failed" });
    }

    [Fact]
    public async Task Ucp_rest_calls_are_counted_by_what_they_answered()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new InstrumentRecorder("tendero.agent.tool_calls");
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        // An empty lookup is a 200 that needs no catalogue; a product with no id
        // is the shop saying no; a well-formed code reaches Postgres, where
        // nothing listens. (A malformed one is refused by validation, and was.)
        await client.PostAsJsonAsync("/ucp/catalog/lookup", new { ids = Array.Empty<string>() }, ct);
        await client.PostAsJsonAsync("/ucp/catalog/product", new { id = "" }, ct);
        await client.PostAsJsonAsync("/ucp/catalog/lookup", new { ids = new[] { "0123456789" } }, ct);

        Assert.Contains(Calls(listener), tags => tags is { surface: "ucp", tool: "lookup_catalog", outcome: "answered" });
        Assert.Contains(Calls(listener), tags => tags is { surface: "ucp", tool: "get_product", outcome: "refused" });
        Assert.Contains(Calls(listener), tags => tags is { surface: "ucp", tool: "lookup_catalog", outcome: "failed" });
    }

    /// <summary>
    /// The route decides the surface, and an MCP or UCP caller is an agent even
    /// though both are anonymous; on the web, only an agent's own token makes one.
    /// </summary>
    [Theory]
    [InlineData("/api/search", false, "human", "web")]
    [InlineData("/api/search", true, "agent", "web")]
    [InlineData("/mcp", false, "agent", "mcp")]
    [InlineData("/ucp/catalog/search", false, "agent", "ucp")]
    public void The_actor_is_read_off_the_request(string path, bool agent, string kind, string surface)
    {
        using var root = new TenderoApiFactory();
        // The principal is its own accessor's business (PrincipalAccessorTests),
        // and reading an agent's token looks its customer up in Postgres. Here
        // it is a given: an agent, or nobody.
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IPrincipalAccessor>(_ => new GivenPrincipal(agent
                ? CommercePrincipal.Anonymous with { Agent = new AgentId("an-agent"), Subject = "an-agent" }
                : CommercePrincipal.Anonymous))));
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Path = path;
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;

        Assert.Equal(new Actor(kind, surface), scope.ServiceProvider.GetRequiredService<IActorAccessor>().Current);
    }

    private static IEnumerable<ToolCallTags> Calls(InstrumentRecorder recorder) =>
        recorder.Of("tendero.agent.tool_calls").Select(tags => new ToolCallTags(
            tags.GetValueOrDefault("surface") as string,
            tags.GetValueOrDefault("tool") as string,
            tags.GetValueOrDefault("outcome") as string));

    private static async Task CallMcp(TenderoApiFactory factory, string tool, object arguments)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await client.PostAsJsonAsync("/mcp",
            new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments } },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _ = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}

internal sealed class GivenPrincipal(CommercePrincipal principal) : IPrincipalAccessor
{
    public CommercePrincipal Current => principal;
}

/// <summary>The three labels of one tool call, for pattern matching.</summary>
public sealed record ToolCallTags(string? surface, string? tool, string? outcome);

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MetricsCollection
{
    public const string Name = "Metrics";
}
