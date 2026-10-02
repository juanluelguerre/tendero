using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

public sealed class AIInvocationsWorkerTests
{
    [Fact]
    public async Task The_worker_writes_what_was_logged()
    {
        var (log, writer, worker) = Compose();
        log.Record(AIInvocationLogTests.Row());
        log.Record(AIInvocationLogTests.Row());

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await writer.Seen(2);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, writer.Rows.Count);
    }

    /// <summary>The database down for a while: a batch is lost, the worker keeps
    /// draining, and the host never stops over bookkeeping.</summary>
    [Fact]
    public async Task A_write_that_fails_is_logged_and_the_next_rows_still_go()
    {
        var (log, writer, worker) = Compose();
        writer.FailNext = true;
        log.Record(AIInvocationLogTests.Row());

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await writer.Attempted(1);
        log.Record(AIInvocationLogTests.Row());
        await writer.Seen(1);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Single(writer.Rows);
    }

    private static (AIInvocationLog, RecordingWriter, AIInvocationsWorker) Compose()
    {
        var log = new AIInvocationLog();
        var writer = new RecordingWriter();
        var scopes = new ServiceCollection().AddSingleton<IAIInvocationWriter>(writer).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return (log, writer, new AIInvocationsWorker(log, scopes, NullLogger<AIInvocationsWorker>.Instance));
    }

    private sealed class RecordingWriter : IAIInvocationWriter
    {
        private int attempts;

        public List<AIInvocation> Rows { get; } = [];

        public bool FailNext { get; set; }

        public Task WriteAsync(IReadOnlyList<AIInvocation> rows, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref this.attempts);
            if (FailNext)
            {
                FailNext = false;
                throw new TimeoutException("the database went away");
            }

            lock (Rows)
                Rows.AddRange(rows);
            return Task.CompletedTask;
        }

        public Task Seen(int count) => Until(() => { lock (Rows) return Rows.Count >= count; });

        public Task Attempted(int count) => Until(() => Volatile.Read(ref this.attempts) >= count);

        private static async Task Until(Func<bool> condition)
        {
            for (var tries = 0; tries < 200 && !condition(); tries++)
                await Task.Delay(10);
            Assert.True(condition());
        }
    }
}
