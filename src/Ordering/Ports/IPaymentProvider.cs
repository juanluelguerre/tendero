using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>
/// What the shop asks a provider to hold.
///
/// <c>IdempotencyKey</c> is the checkout's, passed straight through: retrying an
/// authorisation with the same key must not put a second hold on somebody's
/// card. Every real provider supports this and every fake one has to, or the
/// contract suite is testing a fiction.
///
/// <c>Instrument</c> is an opaque token standing in for a card — never a number.
/// The fake reads it to decide whether to decline, and a real adapter hands it
/// to the provider untouched. It is the one field that must never be logged.
/// </summary>
public sealed record PaymentRequest(
    OrderId OrderId,
    Money Amount,
    string IdempotencyKey,
    string Instrument,
    string? Description = null);

public enum PaymentOutcome
{
    Authorized,

    /// <summary>The provider said no. It is a normal answer, not a failure of
    /// the integration, and the order has a state for it.</summary>
    Declined,

    /// <summary>The provider could not be reached, or answered something we do
    /// not understand. Retryable; a decline is not.</summary>
    Unavailable
}

/// <summary>
/// A hold on somebody's money.
///
/// <c>Outcome</c> and <c>DeclineReason</c> travel together on purpose: a
/// decline that does not say why is a support ticket, and the reason is what
/// the shopper reads.
/// </summary>
public sealed record PaymentAuthorization(
    PaymentOutcome Outcome,
    string? AuthorizationId,
    string? DeclineReason)
{
    public static PaymentAuthorization Authorized(string authorizationId) =>
        new(PaymentOutcome.Authorized, authorizationId, null);

    public static PaymentAuthorization Declined(string reason) =>
        new(PaymentOutcome.Declined, null, reason);

    public static PaymentAuthorization Unavailable(string reason) =>
        new(PaymentOutcome.Unavailable, null, reason);

    public bool Succeeded => Outcome == PaymentOutcome.Authorized;
}

public sealed record PaymentCapture(bool Succeeded, string? CaptureId, string? FailureReason)
{
    public static PaymentCapture Ok(string captureId) => new(true, captureId, null);

    public static PaymentCapture Failed(string reason) => new(false, null, reason);
}

public sealed record PaymentRefund(bool Succeeded, string? RefundId, string? FailureReason)
{
    public static PaymentRefund Ok(string refundId) => new(true, refundId, null);

    public static PaymentRefund Failed(string reason) => new(false, null, reason);
}

/// <summary>
/// Letting go of a hold that was never taken. It is not a refund, and giving it
/// its own type is what stops the two being confused at a call site.
/// </summary>
public sealed record PaymentVoid(bool Succeeded, string? FailureReason)
{
    public static readonly PaymentVoid Ok = new(true, null);

    public static PaymentVoid Failed(string reason) => new(false, reason);
}

/// <summary>What a webhook turned out to be, once its signature held.</summary>
public sealed record PaymentEvent(string Kind, string Reference, OrderId? OrderId, string? Reason);

public enum WebhookVerdict
{
    Accepted,

    /// <summary>The signature did not match. Somebody is forging, or the secret
    /// is wrong; either way nothing in the body is trustworthy.</summary>
    BadSignature,

    /// <summary>Correctly signed, but too old. Replay protection: a signature is
    /// valid forever and a captured request must not be usable tomorrow.</summary>
    Stale,

    /// <summary>Signed, fresh, and not something this shop acts on.</summary>
    Ignored
}

public sealed record WebhookVerification(WebhookVerdict Verdict, PaymentEvent? Event, string? Detail)
{
    public static WebhookVerification Accepted(PaymentEvent paymentEvent) =>
        new(WebhookVerdict.Accepted, paymentEvent, null);

    public static WebhookVerification Rejected(WebhookVerdict verdict, string detail) =>
        new(verdict, null, detail);
}

/// <summary>
/// Money, through one port with keyed adapters — the promise ADR 0003 made in
/// the present tense before any of it existed.
///
/// Four operations and no more. Partial captures, split payments and gift cards
/// are the scope creep this phase was warned about; the port stays the shape a
/// second adapter can honestly implement.
///
/// **Authorise and capture are separate**, and that is not ceremony: the order
/// holds stock between them, so a capture that fails after stock was committed
/// is exactly the compensation path the saga already knows how to run.
///
/// Which is also why there are four operations and not three. An order that
/// authorises and then cannot be filled has to give the hold back, and doing
/// that with <see cref="RefundAsync"/> would credit money nobody ever took.
/// Leaving it to expire — a real authorisation lapses in about a week — is what
/// a shop does when it has no way to say so, and having the way is one method.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Stable, lowercase, kebab. The order stores it beside the
    /// reference, because a reference only means something to the system that
    /// minted it.</summary>
    string Key { get; }

    Task<PaymentAuthorization> AuthorizeAsync(
        PaymentRequest request, CancellationToken cancellationToken = default);

    Task<PaymentCapture> CaptureAsync(
        string authorizationId, Money amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives money back. It takes the CAPTURE reference and not the
    /// authorisation: an authorisation that was never captured is released, not
    /// refunded, and a port that let you confuse them would let a return credit
    /// money that was never taken.
    /// </summary>
    Task<PaymentRefund> RefundAsync(
        string captureId, Money amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases an authorisation that will never be captured.
    ///
    /// It is the compensation arm of checkout: the order authorised, the stock
    /// could not be held, and somebody's card is holding money against a parcel
    /// that will not ship. **Voiding what is already captured must fail** rather
    /// than quietly refunding, because those are two different conversations
    /// with the customer.
    ///
    /// Idempotent: voiding twice is one release. The outbox delivers at least
    /// once, and this runs on a domain event.
    /// </summary>
    Task<PaymentVoid> VoidAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this webhook is really from the provider, and what it says.
    ///
    /// It is on the provider rather than in the endpoint because the scheme is
    /// the provider's: Stripe signs a timestamped payload with HMAC-SHA256 and
    /// its own header format, and the next one will do something else. The
    /// endpoint's job is to pick the adapter and act on the verdict.
    ///
    /// It is synchronous and takes the raw body: verifying a signature over a
    /// re-serialised object is the classic way to build a check that passes for
    /// forgeries.
    /// </summary>
    WebhookVerification VerifyWebhook(
        string rawBody, IReadOnlyDictionary<string, string> headers, DateTimeOffset now);
}

public interface IPaymentProviderRegistry
{
    IReadOnlyCollection<string> Keys { get; }

    IPaymentProvider Get(string key);
}
