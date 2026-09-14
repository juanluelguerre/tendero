using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The outbox, drained by the worker that drains it in production.
///
/// Three suites here used to carry their own copy of <see cref="OutboxProcessor"/>'s
/// loop "without the timer", and a copy tests the events rather than the loop:
/// the retry cap, the isolation of a poisoned message and the error kept in the
/// row could all have changed with every one of these suites still green. They
/// drive the real thing now, one pass at a time, which is the only unit the
/// assertions care about — the saga's compensation arrives on the pass AFTER the
/// one that caused it, and a helper that looped until empty would hide that.
///
/// The real worker catches a handler's exception and marks the message failed,
/// where the copies let it propagate. So a pass that leaves a failed message
/// FAILS the test, with the error the worker stored — a broken handler must
/// stay as loud here as it was.
/// </summary>
internal static class WorkerOutbox
{
    /// <summary>
    /// One pass over EVERYTHING pending: the batch is sized so nothing waits for
    /// a second pass for want of room, which keeps "one pass" meaning what the
    /// saga tests say it means.
    /// </summary>
    private static readonly OutboxOptions OnePass = new() { BatchSize = 100_000 };

    /// <returns>How many messages this pass processed.</returns>
    public static async Task<int> PassAsync(IServiceProvider provider, CancellationToken ct)
    {
        var processedBefore = await CountProcessedAsync(provider, ct);

        await new OutboxProcessor(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(OnePass),
                NullLogger<OutboxProcessor>.Instance)
            .ProcessBatchAsync(ct);

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();

        var failed = await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.ProcessedAt == null && message.Error != null)
            .OrderBy(message => message.OccurredAt)
            .FirstOrDefaultAsync(ct);

        if (failed is not null)
            Assert.Fail($"Outbox message {failed.Type} failed in the worker:{Environment.NewLine}{failed.Error}");

        return await CountProcessedAsync(provider, ct) - processedBefore;
    }

    private static async Task<int> CountProcessedAsync(IServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TenderoDbContext>()
            .OutboxMessages.CountAsync(message => message.ProcessedAt != null, ct);
    }
}
