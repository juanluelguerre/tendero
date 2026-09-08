using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
