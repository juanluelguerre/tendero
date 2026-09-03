using System.Text.Json;
using ElGuerre.Tendero.Ordering.Adapters;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// What every payment provider must honour, whoever writes it.
///
/// ADR 0003 described this suite in the present tense before any of it existed;
/// `docs/testing.md` named the file. Both were promises, and this is the file
/// that keeps them.
///
/// The load-bearing assertions are the two that are easy to skip. **Authorising
/// twice with the same idempotency key must not put a second hold on somebody's
/// card** — every real provider supports it, and a fake that did not would let
/// the whole retry path ship untested. And **a webhook whose signature does not
/// hold is refused**, because an endpoint that trusts an unsigned body is a
/// public API for marking orders paid.
/// </summary>
public abstract class PaymentProviderContractTests
{
    protected abstract IPaymentProvider Provider { get; }

    /// <summary>An instrument this provider authorises. Each adapter has its own
    /// vocabulary of test cards; the contract only needs one that works.</summary>
    protected abstract string WorkingInstrument { get; }

    /// <summary>One it declines. A provider with no way to be told no cannot be
    /// tested for the path a shopper actually hits.</summary>
    protected abstract string DecliningInstrument { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Money Amount = new(38.66m, "EUR");

    protected PaymentRequest Request(string? instrument = null, string key = "checkout-1") =>
        new(OrderId.New(), Amount, key, instrument ?? WorkingInstrument, "Tendero order");

    [Fact]
    public void The_key_is_a_stable_lowercase_identifier()
    {
        Assert.False(string.IsNullOrWhiteSpace(Provider.Key));
        Assert.Equal(Provider.Key.ToLowerInvariant(), Provider.Key);
        Assert.DoesNotContain(' ', Provider.Key);
    }

    // ---------- Authorising ----------

    [Fact]
    public async Task An_authorization_comes_back_with_a_reference()
    {
        var authorization = await Provider.AuthorizeAsync(Request(), Ct);

        Assert.True(authorization.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(authorization.AuthorizationId));
        Assert.Null(authorization.DeclineReason);
    }

    /// <summary>
    /// The one that has to be true or the whole retry path is fiction. A
    /// shopper pressing pay twice, a proxy retrying a timeout, an agent
    /// reissuing a request — all three are the same key, and all three must be
    /// one hold.
    /// </summary>
    [Fact]
    public async Task The_same_idempotency_key_never_produces_a_second_hold()
    {
        var first = await Provider.AuthorizeAsync(Request(key: "checkout-idem"), Ct);
        var second = await Provider.AuthorizeAsync(Request(key: "checkout-idem"), Ct);

        Assert.Equal(first.AuthorizationId, second.AuthorizationId);
    }

    [Fact]
    public async Task Two_different_keys_are_two_different_holds()
    {
        var first = await Provider.AuthorizeAsync(Request(key: "checkout-a"), Ct);
        var second = await Provider.AuthorizeAsync(Request(key: "checkout-b"), Ct);

        Assert.NotEqual(first.AuthorizationId, second.AuthorizationId);
    }

    /// <summary>
    /// A decline is a normal answer, not a broken integration — and it has to
    /// say why, because the reason is what the shopper reads.
    /// </summary>
    [Fact]
    public async Task A_decline_is_an_answer_and_it_says_why()
    {
        var authorization = await Provider.AuthorizeAsync(Request(DecliningInstrument), Ct);

        Assert.False(authorization.Succeeded);
        Assert.Equal(PaymentOutcome.Declined, authorization.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(authorization.DeclineReason));
        Assert.Null(authorization.AuthorizationId);
    }

    [Fact]
    public async Task Authorizing_nothing_is_refused()
    {
        var authorization = await Provider.AuthorizeAsync(
            Request() with { Amount = Money.Zero("EUR") }, Ct);

        Assert.False(authorization.Succeeded);
    }

    [Fact]
    public async Task An_authorization_needs_an_idempotency_key()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => Provider.AuthorizeAsync(Request(key: "  "), Ct));
    }

    // ---------- Capturing and refunding ----------

    [Fact]
    public async Task A_capture_follows_an_authorization_and_gets_its_own_reference()
    {
        var authorization = await Provider.AuthorizeAsync(Request(), Ct);
        var capture = await Provider.CaptureAsync(authorization.AuthorizationId!, Amount, Ct);

        Assert.True(capture.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(capture.CaptureId));

        // Distinct references. Refunding takes the capture, and a port that let
        // you pass either would let a return credit money never taken.
        Assert.NotEqual(authorization.AuthorizationId, capture.CaptureId);
    }

    [Fact]
    public async Task A_refund_follows_a_capture()
    {
        var authorization = await Provider.AuthorizeAsync(Request(), Ct);
        var capture = await Provider.CaptureAsync(authorization.AuthorizationId!, Amount, Ct);
        var refund = await Provider.RefundAsync(capture.CaptureId!, Amount, Ct);

        Assert.True(refund.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(refund.RefundId));
    }

    [Fact]
    public async Task Refunding_nothing_is_refused()
    {
        var authorization = await Provider.AuthorizeAsync(Request(), Ct);
        var capture = await Provider.CaptureAsync(authorization.AuthorizationId!, Amount, Ct);

        Assert.False((await Provider.RefundAsync(capture.CaptureId!, Money.Zero("EUR"), Ct)).Succeeded);
    }

    /// <summary>
    /// Compensation. The order authorised, the stock could not be held, and
    /// somebody's card is holding money against a parcel that will not ship.
    /// </summary>
    [Fact]
    public async Task An_authorization_can_be_released_without_ever_being_captured()
    {
        var authorization = await Provider.AuthorizeAsync(Request(), Ct);

        Assert.True((await Provider.VoidAsync(authorization.AuthorizationId!, Ct)).Succeeded);
    }

    /// <summary>
    /// The outbox delivers at least once and this runs on a domain event, so a
    /// second release has to be free.
    /// </summary>
    [Fact]
    public async Task Releasing_the_same_hold_twice_is_one_release()
    {
        var authorization = await Provider.AuthorizeAsync(Request(), Ct);

        await Provider.VoidAsync(authorization.AuthorizationId!, Ct);

        Assert.True((await Provider.VoidAsync(authorization.AuthorizationId!, Ct)).Succeeded);
    }

    /// <summary>
    /// Voiding a capture must FAIL rather than quietly refund: they are two
    /// different conversations with the customer, and a port that let you
    /// confuse them would let a cancellation credit money that was taken.
    /// </summary>
    [Fact]
    public async Task What_has_been_captured_cannot_be_voided()
    {
        var authorization = await Provider.AuthorizeAsync(Request(), Ct);
        var capture = await Provider.CaptureAsync(authorization.AuthorizationId!, Amount, Ct);

        Assert.False((await Provider.VoidAsync(capture.CaptureId!, Ct)).Succeeded);
    }

    // ---------- Webhooks ----------

    /// <summary>Produce a body this provider would consider valid, signed for
    /// the given instant. Each adapter signs differently; the contract only cares
    /// that the verification agrees with the signing.</summary>
    protected abstract (string Body, IReadOnlyDictionary<string, string> Headers) SignedWebhook(
        string body, DateTimeOffset at);

    protected abstract DateTimeOffset Now { get; }

    protected static string CaptureBody(string reference) =>
        JsonSerializer.Serialize(new { kind = "payment.captured", reference });

    [Fact]
    public void A_correctly_signed_webhook_is_accepted_and_understood()
    {
        var (body, headers) = SignedWebhook(CaptureBody("cap_123"), Now);

        var verification = Provider.VerifyWebhook(body, headers, Now);

        Assert.Equal(WebhookVerdict.Accepted, verification.Verdict);
        Assert.Equal("cap_123", verification.Event!.Reference);
    }

    [Fact]
    public void An_unsigned_webhook_is_refused()
    {
        var verification = Provider.VerifyWebhook(
            CaptureBody("cap_123"), new Dictionary<string, string>(), Now);

        Assert.Equal(WebhookVerdict.BadSignature, verification.Verdict);
        Assert.Null(verification.Event);
    }

    /// <summary>
    /// The signature covers the body. Changing one byte after signing has to
    /// break it, or the check is decoration.
    /// </summary>
    [Fact]
    public void A_webhook_whose_body_was_edited_after_signing_is_refused()
    {
        var (body, headers) = SignedWebhook(CaptureBody("cap_123"), Now);

        var tampered = body.Replace("cap_123", "cap_999", StringComparison.Ordinal);

        Assert.Equal(WebhookVerdict.BadSignature, Provider.VerifyWebhook(tampered, headers, Now).Verdict);
    }

    /// <summary>
    /// Replay protection. A signature is valid forever, so without a timestamp
    /// check a request captured today can be replayed next week — and marking an
    /// order paid twice is the cheapest fraud there is.
    /// </summary>
    [Fact]
    public void A_correctly_signed_but_ancient_webhook_is_refused()
    {
        var longAgo = Now - TimeSpan.FromHours(2);
        var (body, headers) = SignedWebhook(CaptureBody("cap_123"), longAgo);

        Assert.Equal(WebhookVerdict.Stale, Provider.VerifyWebhook(body, headers, Now).Verdict);
    }

    /// <summary>
    /// Signed, fresh, and about something this shop does not act on. It is not
    /// an attack and must not look like one: a log full of "bad signature" for
    /// events we simply ignore is a log nobody reads.
    /// </summary>
    [Fact]
    public void A_signed_webhook_this_shop_does_not_act_on_is_ignored_rather_than_rejected()
    {
        var (body, headers) = SignedWebhook("{\"nothing\":true}", Now);

        Assert.Equal(WebhookVerdict.Ignored, Provider.VerifyWebhook(body, headers, Now).Verdict);
    }
}

public sealed class FakePaymentProviderContractTests : PaymentProviderContractTests
{
    private static readonly TestClock Clock = new();

    private readonly FakePaymentProvider _provider =
        new(Options.Create(new FakePaymentOptions()), Clock);

    protected override IPaymentProvider Provider => _provider;

    protected override string WorkingInstrument => FakePaymentProvider.CardOk;

    protected override string DecliningInstrument => FakePaymentProvider.CardDeclined;

    protected override DateTimeOffset Now => Clock.GetUtcNow();

    protected override (string Body, IReadOnlyDictionary<string, string> Headers) SignedWebhook(
        string body, DateTimeOffset at) =>
        (body, new Dictionary<string, string>
        {
            [FakePaymentProvider.SignatureHeader] = _provider.SignPayload(body, at)
        });

    /// <summary>
    /// The one the fake exists for. A provider being down is retryable and a
    /// decline is not, so they cannot be the same answer — and only a fake can
    /// produce the first one on demand.
    /// </summary>
    [Fact]
    public async Task An_unreachable_provider_is_a_different_answer_from_a_decline()
    {
        var unreachable = await Provider.AuthorizeAsync(
            Request(FakePaymentProvider.CardUnreachable), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentOutcome.Unavailable, unreachable.Outcome);
    }

    /// <summary>
    /// The reason authorise and capture are separate operations at all: stock is
    /// held between them, so a capture that fails after stock was committed is
    /// exactly the compensation path the saga already knows how to run — and
    /// there is no other way to reach it in a test.
    /// </summary>
    [Fact]
    public async Task A_card_can_authorize_and_then_fail_to_capture()
    {
        var ct = TestContext.Current.CancellationToken;

        var authorization = await Provider.AuthorizeAsync(Request(FakePaymentProvider.CardCaptureFails), ct);

        Assert.True(authorization.Succeeded);

        var capture = await Provider.CaptureAsync(authorization.AuthorizationId!, new Money(10m, "EUR"), ct);

        Assert.False(capture.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(capture.FailureReason));
    }

    /// <summary>
    /// A cryptographic RNG would have been the obvious choice for a reference,
    /// and it is exactly what breaks idempotency. The check lives here rather
    /// than in the shared suite because it is a statement about THIS fake's
    /// implementation.
    /// </summary>
    [Fact]
    public async Task The_reference_is_derived_and_not_random()
    {
        var ct = TestContext.Current.CancellationToken;
        var other = new FakePaymentProvider(Options.Create(new FakePaymentOptions()), Clock);

        var mine = await Provider.AuthorizeAsync(Request(key: "same-key"), ct);
        var theirs = await other.AuthorizeAsync(Request(key: "same-key"), ct);

        // Two instances, one answer: nothing is being remembered in a field.
        Assert.Equal(mine.AuthorizationId, theirs.AuthorizationId);
    }
}
