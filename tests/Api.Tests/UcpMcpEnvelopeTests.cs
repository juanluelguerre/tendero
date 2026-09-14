using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElGuerre.Tendero.Tests.Ucp;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The MCP envelope UCP requires, checked against
/// <c>transports/mcp_tool_call.json</c> — the layer between JSON-RPC and the
/// catalogue payload, which is a contract of its own and the one nobody looks
/// at.
///
/// It found a real gap. The specification puts the UCP payload in
/// <c>result.structuredContent</c> and treats <c>result.content</c> as
/// compatibility output; the SDK emits only <c>content</c> unless a tool asks
/// for structured output. Our answers were well-formed MCP and not UCP, which is
/// the shape of every mistake in this phase: plausible, and wrong in a way only
/// the schema says out loud.
///
/// <c>lookup_catalog</c> with no identifiers is the tool used, deliberately: it
/// reaches no database, so the ENVELOPE can be asserted in process while the
/// payloads are proven against real data in the integration suite.
/// </summary>
public sealed class UcpMcpEnvelopeTests
{
    private const string ToolCall = "https://ucp.dev/schemas/transports/mcp_tool_call.json";
    private const string LookupResponse = "https://ucp.dev/schemas/shopping/catalog_lookup.json#/$defs/lookup_response";

    private static async Task<JsonElement> CallAsync(object arguments)
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await client.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = "lookup_catalog", arguments }
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var payload = body
            .Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim())
            .ToArray();

        return JsonDocument.Parse(payload.Length > 0 ? string.Join(string.Empty, payload) : body)
            .RootElement.Clone();
    }

    [Fact]
    public async Task The_tool_call_response_satisfies_the_mcp_transport_envelope()
    {
        var answer = await CallAsync(new { ids = Array.Empty<string>() });

        UcpSchemas.Validate(ToolCall, answer, "A UCP tool-call response");
    }

    /// <summary>
    /// And the payload inside it is the catalogue response, not merely a string
    /// that happens to parse. This is the assertion that would have failed
    /// before <c>UseStructuredContent</c>.
    /// </summary>
    [Fact]
    public async Task The_ucp_payload_travels_in_structured_content()
    {
        var answer = await CallAsync(new { ids = Array.Empty<string>() });

        var structured = answer.GetProperty("result").GetProperty("structuredContent");

        UcpSchemas.Validate(LookupResponse, structured, "The structuredContent of a lookup_catalog answer");
        Assert.Equal("2026-04-08", structured.GetProperty("ucp").GetProperty("version").GetString());
    }

    /// <summary>
    /// `meta` is optional — the schema says so, and this binding used to refuse
    /// without it, which turned every conformant caller into an error. Both
    /// shapes have to work.
    /// </summary>
    [Fact]
    public async Task A_call_with_a_well_formed_agent_profile_is_accepted()
    {
        var answer = await CallAsync(new
        {
            ids = Array.Empty<string>(),
            meta = new Dictionary<string, object>
            {
                ["ucp-agent"] = new { profile = "https://agent.example/profile" }
            }
        });

        UcpSchemas.Validate(ToolCall, answer, "A UCP tool-call response with agent metadata");
        Assert.False(answer.GetProperty("result").TryGetProperty("isError", out var isError) && isError.GetBoolean());
    }

    /// <summary>
    /// Absent is fine; malformed is not. A `ucp-agent` with no `profile` is the
    /// caller getting the reserved member wrong, and it should read like it.
    /// </summary>
    [Fact]
    public async Task A_call_whose_agent_metadata_is_malformed_is_refused()
    {
        var answer = await CallAsync(new
        {
            ids = Array.Empty<string>(),
            meta = new Dictionary<string, object> { ["ucp-agent"] = new { } }
        });

        var result = answer.GetProperty("result");

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains(
            "profile",
            result.GetProperty("content")[0].GetProperty("text").GetString(),
            StringComparison.Ordinal);
    }
}
