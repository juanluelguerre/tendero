using System.Diagnostics;
using ElGuerre.Tendero.TrafficSim.Driving;
using ElGuerre.Tendero.TrafficSim.Model;
using ElGuerre.Tendero.TrafficSim.Queries;
using ElGuerre.Tendero.TrafficSim.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace ElGuerre.Tendero.TrafficSim;

public sealed class SimulationOptions
{
    public const string SectionName = "TrafficSim";

    /// <summary>The run's seed. Unset draws one, and the run logs and tags it so it can be replayed.</summary>
    public int? Seed { get; set; }

    /// <summary>How long a simulated day lasts in real time.</summary>
    public TimeSpan DayLength { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The simulated hour the run starts at; unset is the machine's own hour.</summary>
    public int? StartHour { get; set; }

    /// <summary>Browser sessions at once. Arrivals beyond it wait their turn; nothing is dropped.</summary>
    public int MaxBrowsers { get; set; } = 6;

    public Uri Storefront { get; set; } = new("http://localhost:4200");

    public Uri Backoffice { get; set; } = new("http://localhost:4201");

    public Uri Api { get; set; } = new("http://localhost:5130");

    /// <summary>Stop after this many sessions; unset runs until stopped.</summary>
    public int? Sessions { get; set; }
}

/// <summary>
/// The run: arrivals from the model, a journey drawn for each, and a driver that
/// carries it out — a browser for a person, a protocol for an agent. It runs until
/// stopped (ADR 0037, decision 5: never on by itself, and never generating load).
/// </summary>
public sealed class Simulation(
    IOptions<SimulationOptions> options, IHttpClientFactory clients, ILogger<Simulation> logger)
{
    public const string SourceName = "ElGuerre.Tendero.TrafficSim";

    public static readonly ActivitySource Telemetry = new(SourceName);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var seed = settings.Seed ?? Random.Shared.Next();
        var runId = $"sim-{seed}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var startOfDay = TimeSpan.FromHours(settings.StartHour ?? DateTime.Now.Hour);

        // A short span that says the run began and with what: a span as long as
        // the run would be exported only when the run ends, which is never.
        using (var started = Telemetry.StartActivity("traffic-sim run"))
        {
            started?.SetTag("user_agent.synthetic.type", "test");
            started?.SetTag("synthetic.run_id", runId);
            started?.SetTag("synthetic.run_seed", seed);
            started?.SetTag("synthetic.day_length_s", settings.DayLength.TotalSeconds);
        }

        logger.LogInformation("Run {RunId}: seed {Seed}, a day in {DayLength}, starting at {Hour:00}:00, up to {Browsers} browsers.",
            runId, seed, settings.DayLength, startOfDay.Hours, settings.MaxBrowsers);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var userAgent = await UserAgentAsync(browser);

        var placed = new PlacedOrders();
        var people = new BrowserDriver(browser, settings.Storefront, userAgent, placed);
        var staff = new BackofficeDriver(browser, settings.Backoffice, userAgent);
        var agents = new AgentDriver(clients.CreateClient("api"), settings.Api);
        using var seats = new SemaphoreSlim(settings.MaxBrowsers);
        var running = new List<Task>();
        var clock = Stopwatch.StartNew();

        foreach (var arrival in Arrivals.Draw(seed, settings.DayLength, startOfDay))
        {
            if (settings.Sessions is { } limit && arrival.Index >= limit)
                break;

            var wait = arrival.At - clock.Elapsed;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);

            var journey = Journey.Draw(arrival.Index, Seeds.ForSession(seed, arrival.Index), arrival.Persona, GoldenQueries.In);
            running.RemoveAll(task => task.IsCompleted);
            running.Add(RunSessionAsync(journey, new SyntheticLabel(runId, journey.Persona.Name), people, staff, agents, seats, cancellationToken));

            logger.LogInformation("Session {Index} ({Persona}, {Culture}): {Steps}.",
                journey.Index, journey.Persona.Name, journey.Culture, string.Join(" > ", journey.Steps.Select(step => step.Step)));
        }

        await Task.WhenAll(running);
    }

    private async Task RunSessionAsync(
        Journey journey, SyntheticLabel label, BrowserDriver people, BackofficeDriver staff, AgentDriver agents,
        SemaphoreSlim seats, CancellationToken cancellationToken)
    {
        // A browser for a person and for the shopkeeper; the seats are shared.
        var browserSession = journey.Persona.Kind != PersonaKind.Agent;
        if (browserSession)
            await seats.WaitAsync(cancellationToken);

        // A root span of its own, so a session is one trace however long it waited.
        Activity.Current = null;
        using var activity = Telemetry.StartActivity($"session {journey.Persona.Name}");
        activity?.SetTag("user_agent.synthetic.type", "test");
        activity?.SetTag("synthetic.run_id", label.RunId);
        activity?.SetTag("synthetic.persona", label.Persona);
        activity?.SetTag("synthetic.session", journey.Index);
        activity?.SetTag("synthetic.seed", journey.Seed);
        activity?.SetTag("synthetic.culture", journey.Culture);
        activity?.SetTag("synthetic.steps", journey.Steps.Count);
        if (journey.AbandonmentReason is { } reason)
            activity?.SetTag("synthetic.abandonment_reason", reason);
        if (journey.ReturnReason is { } returned)
            activity?.SetTag("synthetic.return_reason", returned);

        try
        {
            var run = journey.Persona.Kind switch
            {
                PersonaKind.Human => people.RunAsync(journey, label, cancellationToken),
                PersonaKind.Staff => staff.RunAsync(journey, label, cancellationToken),
                _ => agents.RunAsync(journey, label, cancellationToken),
            };
            await run;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One session failing is a session the shop lost, not a reason to stop the day.
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            logger.LogWarning(exception, "Session {Index} ({Persona}) failed.", journey.Index, journey.Persona.Name);
        }
        finally
        {
            if (browserSession)
                seats.Release();
        }
    }

    private static async Task<string> UserAgentAsync(IBrowser browser)
    {
        await using var probe = await browser.NewContextAsync();
        var page = await probe.NewPageAsync();
        return await page.EvaluateAsync<string>("() => navigator.userAgent");
    }
}
