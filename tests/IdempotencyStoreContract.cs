using System.Collections.Concurrent;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Tests;

/// <summary>
/// What every <see cref="IIdempotencyStore"/> does, whichever keeps the keys:
/// the suite the in-memory store the unit tests use and the one on Postgres both
/// inherit, so a test that passes against the first is a statement about the
/// second.
/// </summary>
public abstract class IdempotencyStoreContract
{
    protected abstract Task<IIdempotencyStore> OpenAsync(TimeProvider clock);

    /// <summary>Unavailable infrastructure skips rather than fails (Docker, for the one on Postgres).</summary>
    protected virtual void SkipIfUnavailable()
    {
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_key_nobody_used_is_the_requests_and_its_answer_is_given_back_to_the_same_request()
    {
        SkipIfUnavailable();
        var store = await OpenAsync(new TestClock());

        Assert.IsType<IdempotencyClaim.Fresh>(await store.ClaimAsync("agent:a", "k1", "request-1", Ct));
        await store.CompleteAsync("agent:a", "k1", 201, """{"id":"s1"}""", Ct);

        var again = Assert.IsType<IdempotencyClaim.Replayed>(await store.ClaimAsync("agent:a", "k1", "request-1", Ct));
        Assert.Equal((201, """{"id":"s1"}"""), (again.Status, again.Body));
    }

    [Fact]
    public async Task The_same_key_on_another_request_is_a_mismatch_whether_the_first_finished_or_not()
    {
        SkipIfUnavailable();
        var store = await OpenAsync(new TestClock());

        await store.ClaimAsync("agent:a", "running", "request-1", Ct);
        Assert.IsType<IdempotencyClaim.Mismatched>(await store.ClaimAsync("agent:a", "running", "request-2", Ct));

        await store.ClaimAsync("agent:a", "done", "request-1", Ct);
        await store.CompleteAsync("agent:a", "done", 200, "{}", Ct);
        Assert.IsType<IdempotencyClaim.Mismatched>(await store.ClaimAsync("agent:a", "done", "request-2", Ct));
    }

    [Fact]
    public async Task The_same_request_while_the_first_is_running_is_told_so_and_does_not_run()
    {
        SkipIfUnavailable();
        var store = await OpenAsync(new TestClock());

        Assert.IsType<IdempotencyClaim.Fresh>(await store.ClaimAsync("agent:a", "k1", "request-1", Ct));
        Assert.IsType<IdempotencyClaim.InFlight>(await store.ClaimAsync("agent:a", "k1", "request-1", Ct));
    }

    [Fact]
    public async Task A_released_key_is_free_again_for_any_request()
    {
        SkipIfUnavailable();
        var store = await OpenAsync(new TestClock());

        await store.ClaimAsync("agent:a", "k1", "request-1", Ct);
        await store.ReleaseAsync("agent:a", "k1", Ct);

        Assert.IsType<IdempotencyClaim.Fresh>(await store.ClaimAsync("agent:a", "k1", "request-2", Ct));
    }

    [Fact]
    public async Task A_key_belongs_to_whoever_sent_it()
    {
        SkipIfUnavailable();
        var store = await OpenAsync(new TestClock());

        await store.ClaimAsync("agent:a", "k1", "request-1", Ct);
        await store.CompleteAsync("agent:a", "k1", 200, """{"whose":"a"}""", Ct);

        Assert.IsType<IdempotencyClaim.Fresh>(await store.ClaimAsync("agent:b", "k1", "request-9", Ct));
    }

    /// <summary>
    /// A process that dies between claiming and answering leaves a claim nobody
    /// will finish. It holds the key for <see cref="Idempotency.Lease"/> and no
    /// longer; an answer, once kept, is never taken over.
    /// </summary>
    [Fact]
    public async Task A_claim_nobody_finished_is_taken_over_after_its_lease_and_an_answer_never_is()
    {
        SkipIfUnavailable();
        var clock = new TestClock();
        var store = await OpenAsync(clock);

        await store.ClaimAsync("agent:a", "abandoned", "request-1", Ct);
        await store.ClaimAsync("agent:a", "answered", "request-1", Ct);
        await store.CompleteAsync("agent:a", "answered", 200, "{}", Ct);

        clock.Advance(Idempotency.Lease - TimeSpan.FromSeconds(1));
        Assert.IsType<IdempotencyClaim.InFlight>(await store.ClaimAsync("agent:a", "abandoned", "request-1", Ct));

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.IsType<IdempotencyClaim.Fresh>(await store.ClaimAsync("agent:a", "abandoned", "request-1", Ct));
        // Taken over just now: its lease starts again.
        Assert.IsType<IdempotencyClaim.InFlight>(await store.ClaimAsync("agent:a", "abandoned", "request-1", Ct));

        clock.Advance(TimeSpan.FromDays(30));
        Assert.IsType<IdempotencyClaim.Replayed>(await store.ClaimAsync("agent:a", "answered", "request-1", Ct));
    }

    /// <summary>Two requests racing on one key: one runs.</summary>
    [Fact]
    public async Task Of_many_claims_at_once_exactly_one_is_fresh()
    {
        SkipIfUnavailable();
        var store = await OpenAsync(new TestClock());

        var claims = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => store.ClaimAsync("agent:a", "raced", "request-1", Ct), Ct)));

        Assert.Single(claims.OfType<IdempotencyClaim.Fresh>());
        Assert.Equal(11, claims.OfType<IdempotencyClaim.InFlight>().Count());
    }
}

/// <summary>The keys in a dictionary: what a unit test hands whatever needs a store.</summary>
public sealed class InMemoryIdempotencyStore(TimeProvider? clock = null) : IIdempotencyStore
{
    private readonly ConcurrentDictionary<(string Scope, string Key), IdempotencyRecord> records = new();
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly Lock gate = new();

    public int Kept => this.records.Values.Count(record => record.Status is not null);

    public Task<IdempotencyClaim> ClaimAsync(string scope, string key, string fingerprint, CancellationToken cancellationToken = default)
    {
        lock (this.gate)
        {
            var now = this.clock.GetUtcNow();
            if (!this.records.TryGetValue((scope, key), out var known))
            {
                this.records[(scope, key)] = new IdempotencyRecord { Scope = scope, Key = key, Fingerprint = fingerprint, At = now };
                return Task.FromResult<IdempotencyClaim>(new IdempotencyClaim.Fresh());
            }

            if (known.Fingerprint != fingerprint)
                return Task.FromResult<IdempotencyClaim>(new IdempotencyClaim.Mismatched());
            if (known.Status is { } status)
                return Task.FromResult<IdempotencyClaim>(new IdempotencyClaim.Replayed(status, known.Body!));
            if (now - known.At < Idempotency.Lease)
                return Task.FromResult<IdempotencyClaim>(new IdempotencyClaim.InFlight());

            known.At = now;
            return Task.FromResult<IdempotencyClaim>(new IdempotencyClaim.Fresh());
        }
    }

    public Task CompleteAsync(string scope, string key, int status, string body, CancellationToken cancellationToken = default)
    {
        lock (this.gate)
        {
            var record = this.records[(scope, key)];
            record.Status = status;
            record.Body = body;
        }

        return Task.CompletedTask;
    }

    public Task ReleaseAsync(string scope, string key, CancellationToken cancellationToken = default)
    {
        lock (this.gate)
            this.records.TryRemove((scope, key), out _);

        return Task.CompletedTask;
    }
}
