using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>The store the unit tests hand out keeps the contract the one on Postgres does.</summary>
public sealed class InMemoryIdempotencyStoreTests : IdempotencyStoreContract
{
    protected override Task<IIdempotencyStore> OpenAsync(TimeProvider clock) =>
        Task.FromResult<IIdempotencyStore>(new InMemoryIdempotencyStore(clock));
}

public sealed class IdempotencyFingerprintTests
{
    [Fact]
    public void The_same_request_is_the_same_fingerprint_and_any_other_is_another()
    {
        var one = Idempotency.Fingerprint("update_checkout", "s1", """{"quantity":2}""");

        Assert.Equal(one, Idempotency.Fingerprint("update_checkout", "s1", """{"quantity":2}"""));
        Assert.NotEqual(one, Idempotency.Fingerprint("update_checkout", "s1", """{"quantity":3}"""));
        Assert.NotEqual(one, Idempotency.Fingerprint("update_checkout", "s2", """{"quantity":2}"""));
        Assert.NotEqual(one, Idempotency.Fingerprint("cancel_checkout", "s1", """{"quantity":2}"""));
        Assert.Equal(64, one.Length);
    }

    /// <summary>
    /// Parts are told apart, not run together: `("ab", "c")` and `("a", "bc")`
    /// are two requests, and a fingerprint that joined them would let one key
    /// stand for both.
    /// </summary>
    [Fact]
    public void Where_one_part_ends_is_part_of_the_fingerprint()
    {
        Assert.NotEqual(Idempotency.Fingerprint("ab", "c"), Idempotency.Fingerprint("a", "bc"));
        Assert.NotEqual(Idempotency.Fingerprint("a|1:b"), Idempotency.Fingerprint("a", "b"));
        Assert.NotEqual(Idempotency.Fingerprint("a", ""), Idempotency.Fingerprint("a"));
    }
}
