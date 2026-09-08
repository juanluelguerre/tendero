using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElGuerre.Tendero.Ordering.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The MCP server is mounted in this process and answers the protocol.
///
/// It has no tools yet — those are the next slice — and that is precisely what
/// makes these worth writing now: what is being asserted is the TRANSPORT and
/// the composition, which are what silently do not exist when a `MapX` lands in
/// the wrong place. Five composition failures in this repository's notebook were
/// found by a person rather than by a test, and every one of them looked exactly
/// like this: registered, compiled, green, absent.
/// </summary>
public sealed class McpTransportTests
{
    /// <summary>
    /// Streamable HTTP: the client posts JSON-RPC and must accept either a JSON
    /// answer or an event stream, because the server chooses.
    /// </summary>
    private static HttpClient Connect(TenderoApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return client;
    }

    private static readonly object Initialize = new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "initialize",
        @params = new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "tendero-tests", version = "1.0.0" }
        }
    };

    [Fact]
    public async Task It_answers_initialize_with_the_shop_as_the_server()
    {
        using var factory = new TenderoApiFactory();
        using var client = Connect(factory);

        var response = await client.PostAsJsonAsync("/mcp", Initialize, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(JsonRpcPayload(body));
        var result = document.RootElement.GetProperty("result");

        Assert.Equal("tendero", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            result.GetProperty("protocolVersion").GetString()));
    }

    /// <summary>
    /// Anonymous by explicit decision, not by omission: the read tools serve a
    /// catalogue that a browser reads without a token either. The sibling
    /// assertion — that this is a DECISION — is
    /// <see cref="EndpointAuthorizationTests"/>, which walks every route.
    /// </summary>
    [Fact]
    public void Its_route_is_anonymous_on_purpose()
    {
        using var factory = new TenderoApiFactory();
        _ = factory.CreateClient();

        var mcp = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/mcp", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(mcp);
        Assert.All(mcp, endpoint =>
        {
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Null(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        });
    }

    /// <summary>
    /// And out of the OpenAPI document, which describes the REST API. The
    /// committed document generates the frontend's types and will back the UCP
    /// capability schemas; a JSON-RPC route described as a POST that accepts
    /// anything would make it less true rather than more complete.
    /// </summary>
    [Fact]
    public void Its_route_stays_out_of_the_openapi_document()
    {
        using var factory = new TenderoApiFactory();
        _ = factory.CreateClient();

        var mcp = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/mcp", StringComparison.Ordinal) == true)
            .ToArray();

        // Assert.All over nothing passes, and an unmounted server would leave
        // nothing. Caught by mutation: without this line, deleting the MapMcp
        // call turned two of these three red and left this one green.
        Assert.NotEmpty(mcp);
        Assert.All(mcp, endpoint =>
            Assert.NotNull(endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()));
    }

    /// <summary>
    /// The exact set, so a sixth tool is a deliberate act rather than a drift.
    /// The storefront's WebMCP tools are held to the same assertion, for the
    /// same reason: the tool list IS the contract an agent reads, and it is the
    /// one part of this surface that no compiler checks.
    /// </summary>
    [Fact]
    public async Task It_offers_exactly_these_five_tools()
    {
        using var factory = new TenderoApiFactory();
        using var client = Connect(factory);

        var tools = await Call(client, new { jsonrpc = "2.0", id = 2, method = "tools/list" });

        Assert.Equal(
            ["get_product", "get_return_policy", "list_categories", "list_offers", "search_products"],
            tools.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// One tool, end to end, with no database and no Elasticsearch: the returns
    /// window is a constant read through a handler, so this exercises the whole
    /// path — JSON-RPC in, dispatcher, handler, answer out — on infrastructure
    /// that is not running. The number is compared against the aggregate rather
    /// than typed here, which is the same reason the endpoint exists at all.
    /// </summary>
    [Fact]
    public async Task Calling_a_tool_reaches_the_dispatcher_and_answers()
    {
        using var factory = new TenderoApiFactory();
        using var client = Connect(factory);

        var answer = await Call(client, new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new { name = "get_return_policy", arguments = new { } }
        });

        var content = answer.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        using var policy = JsonDocument.Parse(content!);

        Assert.Equal(
            (int)ReturnRequest.Window.TotalDays,
            policy.RootElement.GetProperty("windowDays").GetInt32());
    }

    /// <summary>
    /// And a refusal, which is the half worth having. The validation step throws
    /// the same exception whoever asked; over HTTP it becomes a ProblemDetails
    /// and here a JSON-RPC error carrying the same sentence. Without that
    /// mapping the agent gets an unhandled exception and learns nothing about
    /// how to ask again.
    /// </summary>
    [Fact]
    public async Task A_query_the_shop_refuses_comes_back_as_a_readable_error()
    {
        using var factory = new TenderoApiFactory();
        using var client = Connect(factory);

        var answer = await Call(client, new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "tools/call",
            // One character: allowed to be empty (that is a browse), refused at
            // one, because a single letter matches so much the answer is noise.
            @params = new { name = "search_products", arguments = new { query = "z" } }
        });

        var result = answer.GetProperty("result");

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains(
            "at least 2 characters",
            result.GetProperty("content")[0].GetProperty("text").GetString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The header reaches the tool, which is the half of P9-4 that could not be
    /// assumed. The tools read `Accept-Language` through `IHttpContextAccessor`,
    /// and whether that flows into a tool invocation is a property of the SDK's
    /// transport rather than of our code — so it is asked rather than trusted.
    ///
    /// `list_categories` is the tool used because it needs no database: the
    /// answer may be an empty list here, and what is being read is the culture
    /// the envelope reports, not the rows.
    /// </summary>
    [Theory]
    [InlineData("en-GB,en;q=0.9", "en")]
    [InlineData("fr-FR", "es")]
    public async Task The_accept_language_header_reaches_the_tools(string header, string expected)
    {
        using var factory = new TenderoApiFactory();
        using var client = Connect(factory);
        client.DefaultRequestHeaders.Add("Accept-Language", header);

        var answer = await Call(client, new
        {
            jsonrpc = "2.0",
            id = 5,
            method = "tools/call",
            @params = new { name = "list_categories", arguments = new { } }
        });

        var content = answer.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        using var payload = JsonDocument.Parse(content!);

        Assert.Equal(expected, payload.RootElement.GetProperty("culture").GetString());
    }

    private static async Task<JsonElement> Call(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/mcp", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return JsonDocument.Parse(JsonRpcPayload(body)).RootElement.Clone();
    }

    /// <summary>
    /// The answer may arrive as one JSON object or as a single server-sent
    /// event carrying it. Both are the same message and the test cares about the
    /// message, not about which frame it travelled in.
    /// </summary>
    private static string JsonRpcPayload(string body)
    {
        var data = body
            .Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim())
            .ToArray();

        return data.Length > 0 ? string.Join(string.Empty, data) : body;
    }
}
