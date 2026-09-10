using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Persistence.Outbox;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The outbox worker, the real one, against a real Postgres.
///
/// Three suites in this project drain the outbox, and all three did it with a
/// copy of this loop "without the timer". The copies prove the events reach
/// their handlers; they cannot prove anything about the loop itself, and the
/// loop is where the delicate rules live: a message that fails must not stop
/// the ones behind it, a poisoned one must stop being retried, and what it
/// failed with must survive for whoever opens the table afterwards.
///
/// The event is a probe of this test's own, so what is measured is the worker
/// and not any context's handler.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OutboxProcessorTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 10, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_message_that_fails_does_not_stop_the_ones_behind_it()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var scope = await ArrangeAsync("outbox_worker_isolation", new OutboxOptions(), ct);

        await scope.EnqueueAsync(ct, Probe("first", fail: true, T0), Probe("second", fail: false, T0.AddSeconds(1)));

        await scope.Processor.ProcessBatchAsync(ct);

        Assert.Equal(["first", "second"], scope.Handler.Attempted);

        var rows = await scope.RowsAsync(ct);
        Assert.NotNull(rows["second"].ProcessedAt);
        Assert.Null(rows["first"].ProcessedAt);
        Assert.Equal(1, rows["first"].Attempts);
    }

    /// <summary>
    /// What is left of a message that exhausted its retries is this column, so
    /// it has to say what threw and where — "Object reference not set to an
    /// instance of an object" with no type and no trace diagnoses nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_failure_is_kept_with_its_type_and_its_stack_trace()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var scope = await ArrangeAsync("outbox_worker_error", new OutboxOptions(), ct);

        await scope.EnqueueAsync(ct, Probe("broken", fail: true, T0));

        await scope.Processor.ProcessBatchAsync(ct);

        var error = (await scope.RowsAsync(ct))["broken"].Error;
        Assert.NotNull(error);
        Assert.Contains(nameof(InvalidOperationException), error);
        Assert.Contains("probe broken refused", error);
        Assert.Contains(nameof(ProbeHandler), error);
    }

    /// <summary>A poisoned queue must not spin on its own: after the cap the
    /// message stays in the table, visible, and is never picked up again.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_poisoned_message_is_retried_up_to_the_cap_and_then_left_alone()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var scope = await ArrangeAsync("outbox_worker_cap", new OutboxOptions { MaxAttempts = 3 }, ct);

        await scope.EnqueueAsync(ct, Probe("poison", fail: true, T0));

        for (var pass = 0; pass < 5; pass++)
            await scope.Processor.ProcessBatchAsync(ct);

        Assert.Equal(3, scope.Handler.Attempted.Count);
        Assert.Equal(3, (await scope.RowsAsync(ct))["poison"].Attempts);
    }

    /// <summary>Oldest first, a batch at a time, and a message once processed is
    /// never delivered again — at-least-once is the guarantee, not
    /// every-time.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Messages_are_delivered_oldest_first_a_batch_at_a_time_and_only_once()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var scope = await ArrangeAsync("outbox_worker_order", new OutboxOptions { BatchSize = 2 }, ct);

        await scope.EnqueueAsync(ct,
            Probe("third", fail: false, T0.AddSeconds(2)),
            Probe("first", fail: false, T0),
            Probe("second", fail: false, T0.AddSeconds(1)));

        await scope.Processor.ProcessBatchAsync(ct);
        Assert.Equal(["first", "second"], scope.Handler.Attempted);

        await scope.Processor.ProcessBatchAsync(ct);
        await scope.Processor.ProcessBatchAsync(ct);
        Assert.Equal(["first", "second", "third"], scope.Handler.Attempted);

        Assert.All((await scope.RowsAsync(ct)).Values, row =>
        {
            Assert.NotNull(row.ProcessedAt);
            Assert.Null(row.Error);
        });
    }

    /// <summary>A row whose event was renamed or moved since it was written is
    /// the same poison with a different cause, and the reason has to say so.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task An_event_type_that_no_longer_exists_is_kept_with_the_reason()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var scope = await ArrangeAsync("outbox_worker_gone", new OutboxOptions(), ct);

        await using (var context = scope.Factory.Create())
        {
            context.OutboxMessages.Add(OutboxMessage.For("Gone.Event, Nowhere", "{}", T0));
            await context.SaveChangesAsync(ct);
        }

        await scope.Processor.ProcessBatchAsync(ct);

        await using var after = scope.Factory.Create();
        var row = await after.OutboxMessages.SingleAsync(ct);
        Assert.Null(row.ProcessedAt);
        Assert.Contains("could not be resolved", row.Error);
    }

    // ---------- Plumbing ----------

    private static ProbeEvent Probe(string label, bool fail, DateTimeOffset at) => new(label, fail, at);

    private async Task<WorkerScope> ArrangeAsync(string database, OutboxOptions options, CancellationToken ct)
    {
        var factory = await postgres.CreateDatabaseAsync(database);

        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        return new WorkerScope(factory, options);
    }

    private sealed class WorkerScope : IAsyncDisposable
    {
        private readonly ServiceProvider provider;

        public TenderoDbContextFactory Factory { get; }
        public ProbeHandler Handler { get; } = new();
        public OutboxProcessor Processor { get; }

        public WorkerScope(TenderoDbContextFactory factory, OutboxOptions options)
        {
            Factory = factory;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTenderoCqrs();
            services.AddTenderoPersistence(factory.ConnectionString);
            services.AddSingleton<IDomainEventHandler<ProbeEvent>>(Handler);

            this.provider = services.BuildServiceProvider(validateScopes: true);

            Processor = new OutboxProcessor(
                this.provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(options),
                NullLogger<OutboxProcessor>.Instance);
        }

        public async Task EnqueueAsync(CancellationToken ct, params ProbeEvent[] events)
        {
            await using var context = Factory.Create();

            foreach (var probe in events)
            {
                context.OutboxMessages.Add(OutboxMessage.For(
                    DomainEventSerializer.TypeNameOf(probe), DomainEventSerializer.Serialize(probe), probe.OccurredAt));
            }

            await context.SaveChangesAsync(ct);
        }

        /// <summary>The rows, keyed by the probe's label.</summary>
        public async Task<Dictionary<string, OutboxMessage>> RowsAsync(CancellationToken ct)
        {
            await using var context = Factory.Create();
            var rows = await context.OutboxMessages.AsNoTracking().ToListAsync(ct);

            return rows.ToDictionary(row =>
                ((ProbeEvent)DomainEventSerializer.Deserialize(row.Type, row.Payload)).Label);
        }

        public async ValueTask DisposeAsync() => await this.provider.DisposeAsync();
    }
}

/// <summary>An event that exists only to be delivered — or to refuse.</summary>
internal sealed record ProbeEvent(string Label, bool Fail, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed class ProbeHandler : IDomainEventHandler<ProbeEvent>
{
    public List<string> Attempted { get; } = [];

    public Task HandleAsync(ProbeEvent domainEvent, CancellationToken ct)
    {
        Attempted.Add(domainEvent.Label);

        return domainEvent.Fail
            ? throw new InvalidOperationException($"probe {domainEvent.Label} refused")
            : Task.CompletedTask;
    }
}
