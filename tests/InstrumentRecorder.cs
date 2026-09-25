using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace ElGuerre.Tendero.Tests;

/// <summary>
/// Listens to some instruments for as long as it lives, and keeps every
/// measurement's labels. A metric is a statement no response carries, so the
/// only way to test one is to listen.
///
/// The instruments are static, so every test in the process records into the
/// same counters: a test that uses this runs in a collection with parallelism
/// off, or it hears its neighbours. Linked as a file, like
/// <c>TestClock</c>.
/// </summary>
public sealed class InstrumentRecorder : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly ConcurrentQueue<(string Name, IReadOnlyDictionary<string, object?> Tags)> heard = new();

    public InstrumentRecorder(params string[] instruments)
    {
        this.listener.InstrumentPublished = (instrument, l) =>
        {
            if (instruments.Contains(instrument.Name))
                l.EnableMeasurementEvents(instrument);
        };
        this.listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags)
                copy[tag.Key] = tag.Value;
            this.heard.Enqueue((instrument.Name, copy));
        });
        this.listener.Start();
    }

    /// <summary>The labels of every measurement of one instrument, in the order they came.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Of(string instrument) =>
        [.. this.heard.Where(entry => entry.Name == instrument).Select(entry => entry.Tags)];

    public void Dispose() => this.listener.Dispose();
}
