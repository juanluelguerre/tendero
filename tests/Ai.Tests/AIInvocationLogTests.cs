using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

public sealed class AIInvocationLogTests
{
    /// <summary>A row lost is better than a request held: a full log refuses at
    /// once and says so, it never waits.</summary>
    [Fact]
    public void A_full_log_refuses_without_waiting()
    {
        var log = new AIInvocationLog(capacity: 1);

        Assert.True(log.Record(Row()));
        Assert.False(log.Record(Row()));
    }

    internal static AIInvocation Row() =>
        new(Guid.CreateVersion7(), "steward.turn", "local", "qwen", null, null, null, 1, 1, 0, 10, AIOutcome.Ok, DateTimeOffset.UnixEpoch);
}
