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
    public async Task It_offers_exactly_these_tools()
    {
        using var factory = new TenderoApiFactory();
        using var client = Connect(factory);

        var tools = await Call(client, new { jsonrpc = "2.0", id = 2, method = "tools/list" });

        Assert.Equal(
            // Sorted, so the assertion reads as a set. Three of these are the UCP
            // catalogue binding, whose names the specification fixes —
            // `search_catalog`, `lookup_catalog` and `get_product`. The rest are
            // this shop's own, including `get_product_detail`, which used to be
            // `get_product` and gave the name up to the protocol.
            [
                "build_kit", "cancel_checkout", "compare_products", "complete_checkout", "create_checkout",
                "get_checkout", "get_product", "get_product_detail", "get_product_specs",
                "get_return_policy", "list_campaigns", "list_categories", "list_kits", "list_offers", "lookup_catalog",
                "search_catalog", "search_products", "update_checkout"
            ],
            tools.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// **What a tool says of itself, which a host reads before it lets a model
    /// call it.** Left unsaid, the specification's defaults speak: not read-only,
    /// and destructive — so eleven tools that only read a public catalogue were
    /// announced as tools that may destroy something, and a careful host asks a
    /// person before each search. Found on 2026-10-13 by an audit against the
    /// specification; no test had read the annotations because none existed.
    ///
    /// The checkout's four that change something say so, and say which cannot be
    /// taken back: completing charges, cancelling ends the session.
    /// </summary>
    [Fact]
    public async Task Every_tool_says_whether_it_only_reads_and_whether_it_can_be_taken_back()
    {
        using var factory = new TenderoApiFactory();
        using var client = Connect(factory);

        var tools = await ListToolsAs(client, "2026-07-28");

        var said = tools.GetProperty("result").GetProperty("tools").EnumerateArray().ToDictionary(
            tool => tool.GetProperty("name").GetString()!,
            tool => tool.TryGetProperty("annotations", out var annotations)
                ? (ReadOnly: Hint(annotations, "readOnlyHint"), Destructive: Hint(annotations, "destructiveHint"),
                    Idempotent: Hint(annotations, "idempotentHint"), OpenWorld: Hint(annotations, "openWorldHint"))
                : (null, null, null, null));

        string[] reads =
        [
            "build_kit", "compare_products", "get_checkout", "get_product", "get_product_detail", "get_product_specs", "get_return_policy",
            "list_campaigns", "list_categories", "list_kits", "list_offers", "lookup_catalog", "search_catalog", "search_products",
        ];
        Assert.All(reads, name => Assert.Equal((true, false, true, false), (said[name].ReadOnly, said[name].Destructive, said[name].Idempotent, said[name].OpenWorld)));

        // Opening and updating a session change it and can be changed again; the
        // same update twice is the same session.
        Assert.Equal((false, false, false, false), (said["create_checkout"].ReadOnly, said["create_checkout"].Destructive, said["create_checkout"].Idempotent, said["create_checkout"].OpenWorld));
        Assert.Equal((false, false, true, false), (said["update_checkout"].ReadOnly, said["update_checkout"].Destructive, said["update_checkout"].Idempotent, said["update_checkout"].OpenWorld));
        // Completing places an order and takes the money; cancelling ends the
        // session. Neither is undone — and both are safe to repeat: the first by
        // its idempotency key, the second because a cancelled session stays so.
        Assert.Equal((false, true, true, false), (said["complete_checkout"].ReadOnly, said["complete_checkout"].Destructive, said["complete_checkout"].Idempotent, said["complete_checkout"].OpenWorld));
        Assert.Equal((false, true, true, false), (said["cancel_checkout"].ReadOnly, said["cancel_checkout"].Destructive, said["cancel_checkout"].Idempotent, said["cancel_checkout"].OpenWorld));

        Assert.Equal(18, said.Count);

        static bool? Hint(JsonElement annotations, string name) =>
            annotations.TryGetProperty(name, out var hint) && hint.ValueKind is JsonValueKind.True or JsonValueKind.False ? hint.GetBoolean() : null;
    }

    /// <summary>
    /// **Every tool's answer has a declared shape**, the shop's own eight
    /// included: `structuredContent` a client can use without parsing prose,
    /// under an `outputSchema` it can validate against — and the same JSON as
    /// text, which the specification asks for because there are clients that
    /// hand a model only `content`.
    /// </summary>
    [Fact]
    public async Task Every_tool_declares_the_shape_of_its_answer_and_answers_in_it_twice()
    {
        using var factory = new TenderoApiFactory();
        using var client = Connect(factory);

        var tools = await ListToolsAs(client, "2026-07-28");
        var without = tools.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Where(tool => !tool.TryGetProperty("outputSchema", out _))
            .Select(tool => tool.GetProperty("name").GetString())
            .ToArray();
        Assert.Empty(without);

        var answer = (await Call(client, new
        {
            jsonrpc = "2.0",
            id = 7,
            method = "tools/call",
            @params = new { name = "get_return_policy", arguments = new { } },
        })).GetProperty("result");

        var structured = answer.GetProperty("structuredContent");
        using var text = JsonDocument.Parse(answer.GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.Equal(JsonValueKind.Object, structured.ValueKind);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(structured.GetRawText()), System.Text.Json.Nodes.JsonNode.Parse(text.RootElement.GetRawText())));
    }

    /// <summary>
    /// The specification wants a tool's `outputSchema` to be an object schema,
    /// and a client validates `tools/list` against it: Claude Code refused the
    /// whole list — "expected object, received boolean" — because the five
    /// checkout tools return a `JsonElement`, whose inferred schema is `true`.
    /// </summary>
    [Fact]
    public async Task Every_output_schema_is_an_object_schema()
    {
        using var factory = new TenderoApiFactory();
        using var client = Connect(factory);

        var tools = await ListToolsAs(client, "2026-07-28");

        var notAnObject = tools.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Where(tool => tool.TryGetProperty("outputSchema", out var schema)
                && (schema.ValueKind != JsonValueKind.Object
                    || !schema.TryGetProperty("type", out var type) || type.GetString() != "object"))
            .Select(tool => tool.GetProperty("name").GetString())
            .ToArray();

        Assert.Empty(notAnObject);
    }

    /// <summary>
    /// `tools/list` as a client on the 2026-07-28 protocol asks it — Claude Code
    /// does: no handshake, the version and the method in headers, and who the
    /// client is in the request's own `_meta`. The SDK answers the older versions
    /// with a different shape, which is why the handshake's test did not see it.
    /// </summary>
    private static async Task<JsonElement> ListToolsAs(HttpClient client, string protocol)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new Dictionary<string, object>
                {
                    ["_meta"] = new Dictionary<string, object>
                    {
                        ["io.modelcontextprotocol/protocolVersion"] = protocol,
                        ["io.modelcontextprotocol/clientCapabilities"] = new { },
                        ["io.modelcontextprotocol/clientInfo"] = new { name = "tendero-tests", version = "1.0.0" },
                    },
                },
            }),
        };
        request.Headers.Add("MCP-Protocol-Version", protocol);
        request.Headers.Add("Mcp-Method", "tools/list");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(JsonRpcPayload(body)).RootElement.Clone();
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

        // And it is the problem a REST refusal is, and nothing around it: a
        // code to branch on, and what is wrong with each argument under its name.
        using var problem = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.Equal("Invalid", problem.RootElement.GetProperty("code").GetString());
        Assert.True(problem.RootElement.GetProperty("errors").EnumerateObject().Any());
    }

    /// <summary>
    /// **An argument misnamed or left out is told which.** Found in a live pass
    /// on 2026-10-13: `get_product_detail` called with `sku` where it takes
    /// `code` answered *An error occurred invoking 'get_product_detail'.* — the
    /// SDK's sentence for a tool that broke, which tells an agent nothing it
    /// can correct. It is the request that is wrong, so it is the same problem
    /// a request that does not validate gets: `Invalid`, and what is missing.
    /// </summary>
    [Theory]
    [InlineData("get_product_detail", "code")]
    [InlineData("get_product", "id")]
    public async Task A_call_that_leaves_out_an_argument_is_told_which(string tool, string missing)
    {
        using var factory = new TenderoApiFactory();
        using var client = Connect(factory);

        var answer = await Call(client, new
        {
            jsonrpc = "2.0",
            id = 9,
            method = "tools/call",
            @params = new { name = tool, arguments = new { sku = "B11COO0301-DEFAULT" } }
        });

        var result = answer.GetProperty("result");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.DoesNotContain("An error occurred invoking", text, StringComparison.Ordinal);

        using var problem = JsonDocument.Parse(text);
        Assert.Equal("Invalid", problem.RootElement.GetProperty("code").GetString());
        Assert.Contains(missing, problem.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
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

    /// <summary>
    /// The 2026-07-28 revision, asked as that revision asks: no handshake, the
    /// version in every request and mirrored in its headers. *Servers MUST
    /// implement `server/discover`*, and *MUST include caching hints* on it and
    /// on a tool list. The SDK answers both — verified against a running server
    /// on 2026-10-13, and held here so an upgrade that stopped is seen. The tool
    /// list's hints are ours: the same list for whoever asks, so `public`, and
    /// fresh for as long as the server says.
    /// </summary>
    [Fact]
    public async Task It_answers_discover_and_says_how_long_its_tool_list_keeps()
    {
        using var factory = new TenderoApiFactory();
        using var client = Connect(factory);

        var discovered = (await Modern(client, "server/discover")).GetProperty("result");
        Assert.Contains("2026-07-28", discovered.GetProperty("supportedVersions").EnumerateArray().Select(version => version.GetString()));
        Assert.True(discovered.GetProperty("ttlMs").GetInt64() >= 0);
        Assert.Contains(discovered.GetProperty("cacheScope").GetString(), new[] { "public", "private" });

        var tools = (await Modern(client, "tools/list")).GetProperty("result");
        Assert.Equal((long)Mcp.TenderoMcpServer.ToolListFreshFor.TotalMilliseconds, tools.GetProperty("ttlMs").GetInt64());
        Assert.Equal("public", tools.GetProperty("cacheScope").GetString());
    }

    /// <summary>A version this server does not speak is refused with the ones it does (`-32022`).</summary>
    [Fact]
    public async Task It_refuses_a_version_it_does_not_speak_and_names_the_ones_it_does()
    {
        using var factory = new TenderoApiFactory();
        using var client = Connect(factory);

        var error = (await Modern(client, "tools/list", version: "1900-01-01", expected: HttpStatusCode.BadRequest)).GetProperty("error");

        Assert.Equal(-32022, error.GetProperty("code").GetInt32());
        Assert.Contains("2026-07-28", error.GetProperty("data").GetProperty("supported").EnumerateArray().Select(version => version.GetString()));
    }

    private static async Task<JsonElement> Modern(HttpClient client, string method, string version = "2026-07-28", HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method,
                @params = new
                {
                    _meta = new Dictionary<string, object>
                    {
                        ["io.modelcontextprotocol/protocolVersion"] = version,
                        ["io.modelcontextprotocol/clientInfo"] = new { name = "tendero-tests", version = "1.0.0" },
                        ["io.modelcontextprotocol/clientCapabilities"] = new { },
                    },
                },
            }),
        };
        request.Headers.Add("MCP-Protocol-Version", version);
        request.Headers.Add("Mcp-Method", method);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);

        return JsonDocument.Parse(JsonRpcPayload(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))).RootElement.Clone();
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
