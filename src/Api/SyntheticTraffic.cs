using System.Diagnostics;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// Simulated traffic says so on the server's own span (ADR 0037, decision 4).
///
/// The simulator sends <c>X-Tendero-Synthetic</c> with its run and W3C baggage
/// with the persona; ASP.NET Core already reads the baggage into the request's
/// activity, and this promotes both to span attributes — baggage alone travels
/// but is never exported — and stamps OpenTelemetry's
/// <c>user_agent.synthetic.type=test</c>. A label, not an identity: it changes
/// no behaviour, and a request that lies about it lies only to the dashboard.
/// </summary>
public static class SyntheticTraffic
{
    public const string Header = "X-Tendero-Synthetic";

    public static IApplicationBuilder UseSyntheticTrafficLabels(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (Activity.Current is { } activity && context.Request.Headers[Header] is { Count: > 0 } run)
            {
                activity.SetTag("user_agent.synthetic.type", "test");
                activity.SetTag("synthetic.run_id", run.ToString());
                if (activity.GetBaggageItem("synthetic.persona") is { } persona)
                    activity.SetTag("synthetic.persona", persona);
            }

            await next(context);
        });
}
