using System.Text.Json;
using ElGuerre.Tendero.Ordering.Features.CheckoutSessions;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using ElGuerre.Tendero.Ucp.Capabilities;
using ElGuerre.Tendero.Ucp.Checkout;
using ElGuerre.Tendero.Ucp.Negotiation;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// UCP's idempotency (the REST binding, *Idempotency-Key*): *When provided, the
/// server MUST store the key with the operation result, return the cached result
/// for duplicate keys whose request body matches the original, and return 409
/// Conflict if the key is reused with a mismatched body.*
///
/// Until 2026-10-13 only completing read the key — and read it as "has this key
/// bought something", with no look at the body: the same key with another
/// instrument was answered the first order, and a create sent twice opened two
/// sessions. Here for both transports, because the operations are where both
/// meet.
/// </summary>
public sealed class UcpCheckoutIdempotencyTests
{
    private static readonly IReadOnlySet<string> Active =
        new HashSet<string> { CheckoutCapability.Name, FulfillmentCapability.Name, DiscountCapability.Name };

    private static readonly UcpCheckoutCall Call =
        new(new UcpNegotiated(Active, new PlatformProfile(new Dictionary<string, IReadOnlySet<string>>())), UcpCheckoutConformanceTests.Links);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UcpCheckoutRequest Buying(int quantity) => new()
    {
        LineItems = [new UcpLineItemRequest { Item = new UcpItemReference { Id = "B05DEFG606-DEFAULT" }, Quantity = quantity }],
    };

    private static UcpCompleteRequest Paying(string token) => new()
    {
        Payment = new UcpPaymentRequest
        {
            Instruments = [new UcpInstrumentRequest { HandlerId = UcpCheckoutMapper.SandboxHandlerId, Credential = new UcpCredentialRequest { Token = token } }],
        },
    };

    private static string Json(UcpAnswer answer) => JsonSerializer.Serialize(answer.Body, answer.Body.GetType());

    [Fact]
    public async Task The_same_create_sent_twice_with_a_key_opens_one_session_and_is_answered_the_same()
    {
        var shop = new Shop();

        var first = await shop.Operations.CreateAsync(Buying(2), "k-create", "es", Call, Ct);
        var again = await shop.Operations.CreateAsync(Buying(2), "k-create", "es", Call, Ct);

        Assert.Equal(1, shop.Sent);
        Assert.Equal((201, 201), (first.Status, again.Status));
        Assert.Equal(Json(first), Json(again));
    }

    [Fact]
    public async Task A_key_reused_with_another_body_is_a_conflict_and_nothing_runs()
    {
        var shop = new Shop();

        await shop.Operations.CreateAsync(Buying(2), "k-create", "es", Call, Ct);
        var other = await shop.Operations.CreateAsync(Buying(3), "k-create", "es", Call, Ct);

        Assert.Equal(1, shop.Sent);
        Assert.Equal((409, "idempotency_key_reused"), (other.Status, Assert.IsType<UcpProtocolError>(other.Body).Code));
    }

    /// <summary>The key is the request's, and the request is the operation and its session too: one key does not cancel what it opened.</summary>
    [Fact]
    public async Task A_key_reused_on_another_operation_or_another_session_is_a_conflict()
    {
        var shop = new Shop();
        var (one, other) = (Guid.NewGuid(), Guid.NewGuid());

        await shop.Operations.UpdateAsync(one, Buying(2), "k", "es", Call, Ct);
        var elsewhere = await shop.Operations.UpdateAsync(other, Buying(2), "k", "es", Call, Ct);
        var cancelling = await shop.Operations.CancelAsync(one, "k", "es", Call, Ct);

        Assert.Equal(1, shop.Sent);
        Assert.Equal((409, 409), (elsewhere.Status, cancelling.Status));
    }

    /// <summary>
    /// The case that buys twice. Completing with the same key and another
    /// instrument used to find "this key already bought" and answer the first
    /// order as if the second request had been it.
    /// </summary>
    [Fact]
    public async Task Completing_twice_with_one_key_charges_once_and_another_instrument_under_that_key_is_a_conflict()
    {
        var shop = new Shop();
        var id = Guid.NewGuid();

        var first = await shop.Operations.CompleteAsync(id, Paying("card_ok"), "k-pay", "es", Call, Ct);
        var retry = await shop.Operations.CompleteAsync(id, Paying("card_ok"), "k-pay", "es", Call, Ct);
        var other = await shop.Operations.CompleteAsync(id, Paying("another_card"), "k-pay", "es", Call, Ct);

        Assert.Equal(1, shop.Sent);
        Assert.Equal(Json(first), Json(retry));
        Assert.Equal(409, other.Status);
    }

    [Fact]
    public async Task Without_a_key_every_request_runs()
    {
        var shop = new Shop();

        await shop.Operations.CreateAsync(Buying(2), null, "es", Call, Ct);
        await shop.Operations.CreateAsync(Buying(2), " ", "es", Call, Ct);

        Assert.Equal(2, shop.Sent);
    }

    /// <summary>
    /// What is kept is what **changed something**. A refusal changed nothing, so
    /// there is nothing to give back and the key is free: an agent told that a
    /// checkout is not ready fixes it and completes with the key it had — it is
    /// not answered "not ready" for a day.
    /// </summary>
    [Fact]
    public async Task An_answer_that_changed_nothing_is_not_kept_and_the_key_can_try_again()
    {
        var shop = new Shop { Outcome = CheckoutSessionOutcome.NotReady };
        var id = Guid.NewGuid();

        var refused = await shop.Operations.CompleteAsync(id, Paying("card_ok"), "k-pay", "es", Call, Ct);
        shop.Outcome = CheckoutSessionOutcome.Ok;
        var placed = await shop.Operations.CompleteAsync(id, Paying("card_ok"), "k-pay", "es", Call, Ct);

        Assert.Equal(2, shop.Sent);
        Assert.NotEqual(Json(refused), Json(placed));
        Assert.Equal(1, shop.Keys.Kept);
    }

    [Fact]
    public async Task A_request_that_failed_frees_its_key()
    {
        var shop = new Shop { Fails = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => shop.Operations.CreateAsync(Buying(2), "k", "es", Call, Ct));
        shop.Fails = false;
        var answered = await shop.Operations.CreateAsync(Buying(2), "k", "es", Call, Ct);

        Assert.Equal((2, 201), (shop.Sent, answered.Status));
    }

    [Fact]
    public async Task One_agents_key_is_not_anothers()
    {
        var keys = new InMemoryIdempotencyStore();
        var (claude, other) = (new Shop(keys, "claude-desktop"), new Shop(keys, "another-agent"));

        await claude.Operations.CreateAsync(Buying(2), "k", "es", Call, Ct);
        var theirs = await other.Operations.CreateAsync(Buying(3), "k", "es", Call, Ct);

        Assert.Equal((1, 1, 201), (claude.Sent, other.Sent, theirs.Status));
    }

    /// <summary>The same request while the first is still running: told so, with a 409, and not run beside it.</summary>
    [Fact]
    public async Task A_duplicate_arriving_while_the_first_runs_is_told_it_is_in_progress()
    {
        var shop = new Shop();
        UcpAnswer? duplicate = null;
        shop.WhileRunning = async () => duplicate = await shop.Operations.CreateAsync(Buying(2), "k", "es", Call, Ct);

        await shop.Operations.CreateAsync(Buying(2), "k", "es", Call, Ct);

        Assert.Equal(1, shop.Sent);
        Assert.Equal((409, "idempotency_in_progress"), (duplicate!.Status, Assert.IsType<UcpProtocolError>(duplicate.Body).Code));
    }

    /// <summary>A key longer than the column that keeps it is the caller's mistake, said before anything is claimed.</summary>
    [Fact]
    public async Task A_key_too_long_to_keep_is_refused()
    {
        var shop = new Shop();

        var answer = await shop.Operations.CreateAsync(Buying(2), new string('k', Idempotency.MaximumKeyLength + 1), "es", Call, Ct);

        Assert.Equal((0, 400), (shop.Sent, answer.Status));

        // And the longest key the column keeps is a key like any other (Stryker,
        // phase 13b's close: `>` read as `>=` survived).
        var longest = await shop.Operations.CreateAsync(Buying(2), new string('k', Idempotency.MaximumKeyLength), "es", Call, Ct);

        Assert.Equal(1, shop.Sent);
        Assert.NotEqual(400, longest.Status);
    }

    /// <summary>The operations over a dispatcher that counts, a store in memory and an agent with a name.</summary>
    private sealed class Shop : ICommandDispatcher, IPrincipalAccessor
    {
        public Shop(InMemoryIdempotencyStore? keys = null, string agent = "claude-desktop")
        {
            Keys = keys ?? new InMemoryIdempotencyStore();
            Current = CommercePrincipal.Anonymous with { Agent = new AgentId(agent), Subject = agent };
            Operations = new UcpCheckoutOperations(this, null!, null!, null!, Keys, this);
        }

        public UcpCheckoutOperations Operations { get; }

        public InMemoryIdempotencyStore Keys { get; }

        public CommercePrincipal Current { get; }

        public int Sent { get; private set; }

        public CheckoutSessionOutcome Outcome { get; set; } = CheckoutSessionOutcome.Ok;

        public bool Fails { get; set; }

        public Func<Task>? WhileRunning { get; set; }

        public async Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
        {
            Sent++;
            if (Fails)
                throw new InvalidOperationException("the database went away");

            if (WhileRunning is { } inside)
            {
                WhileRunning = null;
                await inside();
            }

            // A session that differs each time it is opened: an answer given
            // back from the store is told from one made again.
            var session = UcpCheckoutConformanceTests.Session(CheckoutState.ReadyForComplete) with { Id = Guid.NewGuid().ToString() };
            return (TResult)(object)new CheckoutSessionResult(Outcome, session);
        }
    }
}
