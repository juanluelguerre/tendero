using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using ElGuerre.Tendero.TrafficSim.Model;
using ElGuerre.Tendero.TrafficSim.Scheduling;
using ModelContextProtocol.Client;

namespace ElGuerre.Tendero.TrafficSim.Driving;

/// <summary>
/// An agent persona, over its protocol: MCP through the SDK's own client, UCP
/// through plain HTTP. No browser, no think time beyond the model's pause.
///
/// What it looks at next — which product to open — is drawn from the session's
/// own random stream over what the shop answered, so the choice is reproducible
/// for the same catalogue and never decides where the journey goes.
/// </summary>
public sealed class AgentDriver(HttpClient api, Uri apiBase)
{
    private const string AgentProfile = "https://tendero.elguerre.com/.well-known/ucp-agent-simulated";

    public async Task RunAsync(Journey journey, SyntheticLabel label, CancellationToken cancellationToken)
    {
        var random = new Random(journey.Seed ^ 0x5BD1E995);
        var queries = new Queue<string>(journey.Queries);
        var seen = new List<string>();
        McpClient? mcp = null;

        try
        {
            foreach (var step in journey.Steps)
            {
                await Task.Delay(step.ThinkTime, cancellationToken);
                using var activity = Simulation.Telemetry.StartActivity($"step {step.Step}");

                switch (step.Step)
                {
                    case Step.McpSearch:
                        mcp ??= await ConnectAsync(label, journey.Culture, cancellationToken);
                        seen = Codes(await CallAsync(mcp, "search_products", new() { ["query"] = Next(queries), ["culture"] = journey.Culture }, cancellationToken), "hits", "code");
                        break;

                    case Step.McpProduct when seen.Count > 0:
                        mcp ??= await ConnectAsync(label, journey.Culture, cancellationToken);
                        await CallAsync(mcp, "get_product_detail", new() { ["code"] = seen[random.Next(seen.Count)], ["culture"] = journey.Culture }, cancellationToken);
                        break;

                    case Step.UcpSearch:
                        seen = Codes(await PostAsync("/ucp/catalog/search", new { query = Next(queries), meta = Meta() }, label, journey.Culture, cancellationToken), "products", "id");
                        break;

                    case Step.UcpLookup when seen.Count > 0:
                        await PostAsync("/ucp/catalog/lookup", new { ids = new[] { seen[random.Next(seen.Count)] }, meta = Meta() }, label, journey.Culture, cancellationToken);
                        break;

                    default:
                        // Nothing to look at yet: the step happens, with nothing in it.
                        activity?.SetTag("synthetic.step.skipped", true);
                        break;
                }
            }
        }
        finally
        {
            if (mcp is not null)
                await mcp.DisposeAsync();
        }
    }

    private static string Next(Queue<string> queries) => queries.Count > 0 ? queries.Dequeue() : "";

    private static object Meta() => new Dictionary<string, object> { ["ucp-agent"] = new { profile = AgentProfile } };

    private async Task<McpClient> ConnectAsync(SyntheticLabel label, string culture, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(label.Headers) { ["Accept-Language"] = culture };
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(apiBase, "/mcp"),
            Name = "tendero-traffic-sim",
            AdditionalHeaders = headers,
        });

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    private static async Task<JsonElement?> CallAsync(
        McpClient mcp, string tool, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mcp.CallToolAsync(tool, arguments, cancellationToken: cancellationToken);
            var text = result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().FirstOrDefault()?.Text;
            return result.IsError == true || text is null ? null : JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (ModelContextProtocol.McpException exception)
        {
            // The shop said no — a query too short, say. That is traffic too.
            Activity.Current?.SetTag("synthetic.refused", exception.Message);
            return null;
        }
    }

    private async Task<JsonElement?> PostAsync(string path, object body, SyntheticLabel label, string culture, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(apiBase, path)) { Content = JsonContent.Create(body) };
        foreach (var (name, value) in label.Headers)
            request.Headers.TryAddWithoutValidation(name, value);
        request.Headers.AcceptLanguage.ParseAdd(culture);

        using var response = await api.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).Clone()
            : null;
    }

    /// <summary>The identifiers in an answer, wherever the protocol puts them: <c>hits[].code</c>, <c>products[].id</c>.</summary>
    private static List<string> Codes(JsonElement? answer, string array, string field)
    {
        if (answer is not { } root)
            return [];

        // MCP's search answers inside an envelope ({ culture, result: { hits } }).
        var container = root.TryGetProperty("result", out var result) ? result : root;
        return container.TryGetProperty(array, out var items) && items.ValueKind == JsonValueKind.Array
            ? [.. items.EnumerateArray().Select(item => item.TryGetProperty(field, out var value) ? value.GetString() : null).OfType<string>()]
            : [];
    }
}
