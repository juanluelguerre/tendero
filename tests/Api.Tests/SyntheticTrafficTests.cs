using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// Simulated traffic is labelled on the server's own span (ADR 0037): the run
/// from the header, the persona from the baggage, and OpenTelemetry's
/// <c>user_agent.synthetic.type</c>. A request without the header is left alone.
/// Serial, because the listener hears every request span in the process.
/// </summary>
[Collection(MetricsCollection.Name)]
public sealed class SyntheticTrafficTests
{
    [Fact]
    public async Task A_simulated_request_says_so_on_its_span()
    {
        var spans = await RequestSpansAsync(request =>
        {
            request.Headers.Add("X-Tendero-Synthetic", "sim-42");
            request.Headers.Add("baggage", "synthetic.run_id=sim-42,synthetic.persona=searcher");
        });

        var span = Assert.Single(spans, s => s.GetTagItem("synthetic.run_id") is "sim-42");
        Assert.Equal("test", span.GetTagItem("user_agent.synthetic.type"));
        Assert.Equal("searcher", span.GetTagItem("synthetic.persona"));
    }

    [Fact]
    public async Task A_request_from_anybody_else_carries_no_label()
    {
        var spans = await RequestSpansAsync(_ => { });

        Assert.NotEmpty(spans);
        Assert.All(spans, span => Assert.Null(span.GetTagItem("user_agent.synthetic.type")));
    }

    private static async Task<IReadOnlyList<Activity>> RequestSpansAsync(Action<HttpRequestMessage> label)
    {
        var heard = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            // Collected when the span STARTS, not when it stops: the server
            // closes its span after the response has gone, so a test reading
            // stopped spans right after the response raced it — green on a
            // laptop, empty on CI. The labels are set before the response, on
            // the same object, so reading them afterwards is safe.
            ActivityStarted = heard.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/telemetry/config");
        label(request);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        return [.. heard.Where(span => span.Kind == ActivityKind.Server)];
    }
}
