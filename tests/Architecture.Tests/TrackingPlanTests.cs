using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// The tracking plan (ADR 0036) and the code agree, at both ends.
///
/// `docs/telemetry/tracking-plan.json` says which signal proves each feature is
/// used and worth it. The browser half is typed from it — an unplanned event does
/// not compile — but a type cannot say that a planned event is emitted somewhere,
/// and nothing types the backend's meters at all. So this holds both directions:
/// every metric a Tendero meter creates and every event the frontends emit is in
/// the plan, and everything in the plan that is not marked <c>planned</c> is
/// emitted. A <c>planned</c> signal must NOT be emitted yet: the day it is, the
/// mark comes off, or the plan goes on promising work that is done.
///
/// The backend is read by reflection, not text — a counter named through a
/// constant (<c>HybridSearchStrategy.CounterName</c>) is invisible to a regex. It
/// finds instruments held in static fields, which is how every one is declared,
/// and a rule below keeps it that way. The frontend is read as text, like
/// <see cref="FrontendInstallerWiringTests"/>: the alternative is running the
/// TypeScript compiler from a .NET test.
/// </summary>
public sealed class TrackingPlanTests
{
    private static readonly Plan ThePlan = JsonSerializer.Deserialize<Plan>(
        File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "telemetry", "tracking-plan.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static readonly string[] Areas = ["storefront", "backoffice", "agents", "platform"];
    private static readonly string[] Instruments = ["counter", "histogram", "updowncounter", "gauge"];

    private static IEnumerable<Signal> AllSignals => ThePlan.Events.Concat(ThePlan.Metrics);

    // ------------------------------------------------------------- the plan's own rules

    [Fact]
    public void Every_feature_asks_its_question_and_names_the_decision()
    {
        var incomplete = ThePlan.Features
            .Where(feature => string.IsNullOrWhiteSpace(feature.Question)
                || string.IsNullOrWhiteSpace(feature.Decision)
                || !Areas.Contains(feature.Area))
            .Select(feature => feature.Name)
            .ToArray();

        Assert.True(incomplete.Length == 0,
            $"Features with no question, no decision or an unknown area: {string.Join(", ", incomplete)}.");
    }

    [Fact]
    public void A_feature_with_no_signal_says_why_and_one_with_signals_does_not_need_to()
    {
        var silent = ThePlan.Features
            .Where(feature => feature.Signals.Length == 0 == string.IsNullOrWhiteSpace(feature.Gap))
            .Select(feature => feature.Name)
            .ToArray();

        Assert.True(silent.Length == 0,
            $"Each feature has signals or a gap saying why it has none, never both and never neither: {string.Join(", ", silent)}.");
    }

    [Fact]
    public void Every_signal_answers_a_feature_and_every_feature_names_real_signals()
    {
        var names = AllSignals.Select(signal => signal.Name).ToHashSet(StringComparer.Ordinal);
        var claimed = ThePlan.Features.SelectMany(feature => feature.Signals).ToHashSet(StringComparer.Ordinal);

        // A signal no feature claims answers nobody's question: it does not enter.
        Assert.Empty(names.Except(claimed).Order());
        Assert.Empty(claimed.Except(names).Order());
        Assert.Equal(names.Count, AllSignals.Count());
    }

    [Fact]
    public void Signals_are_named_by_the_conventions_of_their_kind()
    {
        var events = ThePlan.Events.Where(e => !Regex.IsMatch(e.Name, "^[a-z]+(_[a-z]+)+$")).Select(e => e.Name);
        var metrics = ThePlan.Metrics.Where(m => !Regex.IsMatch(m.Name, @"^tendero(\.[a-z_]+)+$")).Select(m => m.Name);

        Assert.Empty(events);
        Assert.Empty(metrics);
    }

    [Fact]
    public void Every_metric_declares_an_instrument_and_a_unit()
    {
        var incomplete = ThePlan.Metrics
            .Where(metric => !Instruments.Contains(metric.Instrument) || string.IsNullOrWhiteSpace(metric.Unit))
            .Select(metric => metric.Name);

        Assert.Empty(incomplete);
    }

    [Fact]
    public void A_planned_signal_names_the_task_that_will_emit_it()
    {
        var vague = AllSignals
            .Where(signal => signal.Planned is not null && !Regex.IsMatch(signal.Planned, @"^P\d+[a-z]?-\d+$"))
            .Select(signal => signal.Name);

        Assert.Empty(vague);
    }

    /// <summary>
    /// No attribute identifies a person, a product or an order (ADR 0036): in
    /// Prometheus every distinct value is a series, and in any store it is
    /// personal data the moment it points at somebody.
    /// </summary>
    [Fact]
    public void No_attribute_is_an_identifier()
    {
        var identifiers = AllSignals
            .SelectMany(signal => signal.Attributes.Keys.Select(key => $"{signal.Name}: {key}"))
            .Where(entry => Regex.IsMatch(entry, @"(^|[._:\s])(id|[a-z]+_id|[a-z]+Id)$|\.id$"))
            .ToArray();

        Assert.True(identifiers.Length == 0, $"Attributes that name an id: {string.Join(", ", identifiers)}.");
    }

    // ------------------------------------------------------------- the backend

    [Fact]
    public void Every_tendero_instrument_is_in_the_plan_and_every_live_metric_exists()
    {
        var created = TenderoInstruments().Select(instrument => instrument.Name).ToHashSet(StringComparer.Ordinal);
        var live = ThePlan.Metrics.Where(m => m.Planned is null).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var planned = ThePlan.Metrics.Where(m => m.Planned is not null).Select(m => m.Name);

        Assert.NotEmpty(created);
        Assert.True(!planned.Intersect(created).Any(),
            $"Metrics marked planned that are emitted — take the mark off: {string.Join(", ", planned.Intersect(created).Order())}.");
        Assert.True(!created.Except(live).Any(),
            $"Instruments no feature asked for — add them to docs/telemetry/tracking-plan.json with the question they answer: {string.Join(", ", created.Except(live).Order())}.");
        Assert.True(!live.Except(created).Any(),
            $"Metrics the plan says are emitted and no meter creates: {string.Join(", ", live.Except(created).Order())}.");
    }

    [Fact]
    public void Every_tendero_instrument_has_the_kind_and_unit_the_plan_declares()
    {
        var declared = ThePlan.Metrics.ToDictionary(metric => metric.Name, StringComparer.Ordinal);

        var disagreements = TenderoInstruments()
            .Where(instrument => declared.ContainsKey(instrument.Name))
            .Select(instrument => (instrument, metric: declared[instrument.Name]))
            .Where(pair => KindOf(pair.instrument) != pair.metric.Instrument || pair.instrument.Unit != pair.metric.Unit)
            .Select(pair => $"{pair.instrument.Name} is a {KindOf(pair.instrument)} in '{pair.instrument.Unit}', the plan says a {pair.metric.Instrument} in '{pair.metric.Unit}'")
            .ToArray();

        Assert.True(disagreements.Length == 0, string.Join("; ", disagreements));
    }

    /// <summary>
    /// The reflection above sees static fields only. An instrument or a meter held
    /// in an instance field would be a metric this test cannot see, so there is
    /// none; the day one is needed (an <c>IMeterFactory</c>), this rule and the
    /// discovery change together.
    /// </summary>
    [Fact]
    public void Instruments_and_meters_are_static_fields()
    {
        var instance = Solution.All
            .SelectMany(LoadableTypes)
            .SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => typeof(Instrument).IsAssignableFrom(field.FieldType) || field.FieldType == typeof(Meter))
                .Select(field => $"{type.FullName}.{field.Name}"))
            .ToArray();

        Assert.True(instance.Length == 0, $"Instruments or meters in instance fields: {string.Join(", ", instance)}.");
    }

    // ------------------------------------------------------------- the frontend

    [Fact]
    public void Every_event_the_frontends_emit_is_live_in_the_plan_and_every_live_event_is_emitted()
    {
        var emitted = FrontendCalls().Select(call => call.Name).ToHashSet(StringComparer.Ordinal);
        var live = ThePlan.Events.Where(e => e.Planned is null).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

        Assert.True(!emitted.Except(live).Any(),
            $"Events emitted and not live in the plan: {string.Join(", ", emitted.Except(live).Order())}.");
        Assert.True(!live.Except(emitted).Any(),
            $"Events the plan says are emitted and no frontend code emits: {string.Join(", ", live.Except(emitted).Order())}.");
    }

    /// <summary>The scan reads the name as a literal; a name held in a variable would slip past it.</summary>
    [Fact]
    public void Every_frontend_event_is_named_by_a_literal()
    {
        var dynamic = FrontendCalls().Where(call => call.Name is null).Select(call => call.File).ToArray();

        Assert.True(dynamic.Length == 0, $"trackEvent called with a computed name in: {string.Join(", ", dynamic)}.");
    }

    // ------------------------------------------------------------- discovery

    private static IEnumerable<Instrument> TenderoInstruments() =>
        Solution.All
            .SelectMany(LoadableTypes)
            .Where(type => !type.ContainsGenericParameters)
            .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(field => typeof(Instrument).IsAssignableFrom(field.FieldType))
            .Select(field => (Instrument)field.GetValue(null)!)
            .Where(instrument => TelemetrySources.All.Contains(instrument.Meter.Name));

    /// <summary><c>Counter`1</c> → <c>counter</c>, <c>ObservableGauge`1</c> → <c>gauge</c>.</summary>
    private static string KindOf(Instrument instrument) =>
        instrument.GetType().Name.Split('`')[0].Replace("Observable", "", StringComparison.Ordinal).ToLowerInvariant();

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private static IEnumerable<(string File, string? Name)> FrontendCalls()
    {
        var frontend = Path.Combine(RepositoryRoot(), "frontend");
        var sources = new[] { "apps", "libs" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(frontend, folder), "*.ts", SearchOption.AllDirectories))
            .Where(file => !file.EndsWith(".spec.ts", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (var file in sources)
        {
            foreach (Match call in Regex.Matches(File.ReadAllText(file), @"\btrackEvent\(\s*(?:'(?<name>[^']*)'|(?<other>[^\s)]))"))
            {
                yield return (Path.GetRelativePath(frontend, file), call.Groups["name"].Success ? call.Groups["name"].Value : null);
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tendero.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find Tendero.slnx above the test's output directory.");
    }

    private sealed record Plan(Feature[] Features, Signal[] Events, Signal[] Metrics);

    private sealed record Feature(string Name, string Area, string Question, string Decision, string[] Signals, string? Gap);

    private sealed record Signal(
        string Name,
        string Description,
        string? Planned,
        string? Instrument,
        string? Unit,
        Dictionary<string, JsonElement> Attributes);
}
