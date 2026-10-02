using System.Diagnostics.Metrics;
using System.Threading.Channels;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.AI;

/// <summary>
/// The rows on their way to the database, in process. Bounded, and full means the
/// newest row is dropped and counted: metering must never hold the request it
/// measures (Steward spec, *The models*).
/// </summary>
public sealed class AIInvocationLog(int capacity = 10_000)
{
    private static readonly Meter Meter = new(TelemetrySources.AI);

    private static readonly Counter<long> Dropped = Meter.CreateCounter<long>(
        "tendero.ai.invocations.dropped", unit: "{row}", description: "Model-call rows dropped because the log was full.");

    // Wait, with TryWrite: a full channel refuses at once. The other modes make
    // TryWrite succeed by silently dropping some other row.
    private readonly Channel<AIInvocation> channel = Channel.CreateBounded<AIInvocation>(
        new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ChannelReader<AIInvocation> Reader => this.channel.Reader;

    public bool Record(AIInvocation row)
    {
        if (this.channel.Writer.TryWrite(row))
            return true;

        Dropped.Add(1);
        return false;
    }
}
