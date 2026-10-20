using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Ordering.Adapters;

public sealed class FakePaymentOptions
{
    public const string SectionName = "Payments:Fake";

    /// <summary>
    /// The shared secret the webhook is signed with. A development default so a
    /// fresh clone works; a real deployment overrides it, and the endpoint
    /// refuses an unsigned request either way.
    /// </summary>
    public string WebhookSecret { get; set; } = "tendero-development-webhook-secret";

    /// <summary>
    /// How far out of date a signed payload may be. Five minutes is what Stripe
    /// uses, and the number matters: without it a signature is valid forever and
    /// a captured request can be replayed tomorrow.
    /// </summary>
    public TimeSpan Tolerance { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Money, faked in process — the default provider, and the one the demo runs on.
///
/// It exists for the reason `dev-issuer` exists: the real path has to run from
/// day one without standing up a dependency. Everything around it is production
/// code — the port, the order's payment record, the webhook endpoint, the
/// signature check — and only the thing that says yes or no is pretend.
///
/// **Failure is injected through the instrument, not through configuration.**
/// A flag that makes every payment fail is useless for a demo; what is wanted is
/// one card that declines and one that works, in the same session, chosen at the
/// moment of paying:
///
/// <list type="bullet">
///   <item><c>card-ok</c> — authorises.</item>
///   <item><c>card-declined</c> — declined, with a reason a shopper can read.</item>
///   <item><c>card-unreachable</c> — the provider is down. Retryable, and a different path from a decline.</item>
///   <item><c>card-capture-fails</c> — authorises and then fails to capture, which is the compensation path nobody tests.</item>
/// </list>
///
/// That last one is the point of separating authorise from capture at all: stock
/// is held between them, and a capture that fails after stock was committed is
/// exactly what the saga's compensation arm exists for.
/// </summary>
public sealed class FakePaymentProvider(
    IOptions<FakePaymentOptions> options, TimeProvider clock) : IPaymentProvider
{
    public const string Key = "fake";

    public const string CardOk = "card-ok";
    public const string CardDeclined = "card-declined";
    public const string CardUnreachable = "card-unreachable";
    public const string CardCaptureFails = "card-capture-fails";

    /// <summary>A second card to keep, so a wallet has more than one way to pay (P15-4).</summary>
    public const string CardMastercard = "card-mastercard";

    /// <summary>Kept as already past its date: a card that expired since it was saved.</summary>
    public const string CardExpired = "card-expired";

    /// <summary>The header the fake signs with. Named after itself rather than
    /// after Stripe, so nobody reads this adapter as a Stripe integration.</summary>
    public const string SignatureHeader = "X-Tendero-Signature";

    string IPaymentProvider.Key => Key;

    public Task<PaymentAuthorization> AuthorizeAsync(
        PaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);

        if (request.Amount.IsNegative || request.Amount.IsZero)
            return Task.FromResult(PaymentAuthorization.Declined("An authorization needs a positive amount."));

        // A kept card is charged by its token: what it was made from decides the
        // answer, once the token is known to be live and in date.
        var instrument = request.Instrument;
        if (instrument.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            if (!TryReadToken(instrument, out var card, out var expires) || !Cards.ContainsKey(card))
                return Task.FromResult(PaymentAuthorization.Declined("This saved card is not one this provider keeps."));
            if (this.forgotten.ContainsKey(instrument))
                return Task.FromResult(PaymentAuthorization.Declined("This saved card was removed."));
            if (expires.HasExpiredAt(clock.GetUtcNow()))
                return Task.FromResult(PaymentAuthorization.Declined("The card has expired."));

            instrument = card;
        }

        return Task.FromResult(instrument switch
        {
            CardDeclined => PaymentAuthorization.Declined("The card was declined by the issuer."),
            CardUnreachable => PaymentAuthorization.Unavailable("The payment provider did not answer."),

            // Derived from the idempotency key rather than random: authorising
            // twice with the same key returns the SAME reference, which is what
            // the word means and what the contract suite checks. The instrument
            // only decides whether the marker is appended, so the same card with
            // two keys still gets two authorisations.
            _ => PaymentAuthorization.Authorized(Reference(
                "auth",
                request.IdempotencyKey,
                marked: instrument == CardCaptureFails))
        });
    }

    /// <summary>
    /// Keeps one of the test cards (P15-4), as a real provider keeps what its own
    /// card field collected: a token, and what may be shown. Expiry is counted
    /// from the shop's clock, so the cards stay in date however long the demo runs
    /// — all but <see cref="CardExpired"/>, which stands for a card that expired
    /// since it was kept.
    ///
    /// The token carries which test card it stands for and when it expires, so the
    /// fake stays stateless for everything but forgetting. It is a fake talking to
    /// itself; a real token is opaque, and nothing outside this class reads one.
    /// </summary>
    public Task<InstrumentSaving> SaveInstrumentAsync(string instrument, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrument);

        if (instrument == CardUnreachable)
            return Task.FromResult(InstrumentSaving.Unavailable("The payment provider did not answer."));

        if (!Cards.TryGetValue(instrument, out var card))
            return Task.FromResult(InstrumentSaving.Refused("This is not a card this provider can keep."));

        var now = clock.GetUtcNow();
        var expires = now.AddMonths(card.MonthsLeft);
        var saved = new SavedInstrument(
            $"{TokenPrefix}{Guid.NewGuid():N}_{instrument}_{expires.Year:D4}{expires.Month:D2}",
            card.Brand, card.Last4, expires.Month, expires.Year);

        return Task.FromResult(InstrumentSaving.Saved(saved));
    }

    /// <summary>
    /// Forgets a token for as long as this process lives — the one state the fake
    /// keeps, and enough: the wallet deletes its row in the same change, so a
    /// forgotten token is never offered again after a restart either.
    /// </summary>
    public Task<InstrumentForgetting> ForgetInstrumentAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        this.forgotten.TryAdd(token, 0);
        return Task.FromResult(InstrumentForgetting.Forgotten);
    }

    public Task<PaymentCapture> CaptureAsync(
        string authorizationId, Money amount, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationId);

        // The instrument is long gone by capture time, so the failing card marks
        // its authorisation instead. It is a fake talking to itself, and saying
        // so is cheaper than pretending there is a provider-side state machine.
        if (authorizationId.EndsWith(CaptureFailsMarker, StringComparison.Ordinal))
            return Task.FromResult(PaymentCapture.Failed("The capture was rejected by the issuer."));

        return Task.FromResult(amount.IsNegative || amount.IsZero
            ? PaymentCapture.Failed("A capture needs a positive amount.")
            : PaymentCapture.Ok(Reference("cap", authorizationId)));
    }

    public Task<PaymentRefund> RefundAsync(
        string captureId, Money amount, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureId);

        return Task.FromResult(amount.IsNegative || amount.IsZero
            ? PaymentRefund.Failed("A refund needs a positive amount.")
            : PaymentRefund.Ok(Reference("ref", captureId + amount.Amount.ToString(CultureInfo.InvariantCulture))));
    }

    /// <summary>
    /// Releasing a hold. The fake keeps no state, so "already captured" is
    /// decided the only honest way available to it: a capture reference is not
    /// an authorisation reference, and passing one here is the mistake the
    /// method exists to catch.
    /// </summary>
    public Task<PaymentVoid> VoidAsync(
        string authorizationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationId);

        return Task.FromResult(authorizationId.StartsWith("auth_", StringComparison.Ordinal)
            ? PaymentVoid.Ok
            : PaymentVoid.Failed($"'{authorizationId}' is not an authorization this provider can void."));
    }

    /// <summary>
    /// The same scheme Stripe uses, because copying a real one is the only way
    /// the check is worth writing: <c>t=&lt;unix&gt;,v1=&lt;hex hmac&gt;</c>
    /// over <c>&lt;t&gt;.&lt;body&gt;</c>.
    ///
    /// Three refusals, three different verdicts. A bad signature is somebody
    /// forging; a stale one is somebody replaying; an unknown kind is neither
    /// and must not look like an attack in the logs.
    /// </summary>
    public WebhookVerification VerifyWebhook(
        string rawBody, IReadOnlyDictionary<string, string> headers, DateTimeOffset now)
    {
        if (!headers.TryGetValue(SignatureHeader, out var header) || string.IsNullOrWhiteSpace(header))
            return WebhookVerification.Rejected(WebhookVerdict.BadSignature, "The signature header is missing.");

        if (!TryParse(header, out var timestamp, out var signature))
            return WebhookVerification.Rejected(WebhookVerdict.BadSignature, "The signature header is malformed.");

        var expected = Sign(options.Value.WebhookSecret, timestamp, rawBody);

        // Fixed-time comparison. A string equality here leaks the signature one
        // byte at a time to anybody patient enough to measure.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(signature), Encoding.ASCII.GetBytes(expected)))
            return WebhookVerification.Rejected(WebhookVerdict.BadSignature, "The signature does not match.");

        var age = now - DateTimeOffset.FromUnixTimeSeconds(timestamp);

        if (age.Duration() > options.Value.Tolerance)
            return WebhookVerification.Rejected(
                WebhookVerdict.Stale, $"The payload is {age.Duration().TotalMinutes:F0} minutes out of date.");

        return Interpret(rawBody);
    }

    /// <summary>
    /// Signs a payload the way this provider expects it. It is public because
    /// the contract suite and the demo both need to produce a valid webhook, and
    /// a test that reimplemented the signing would be testing its own copy.
    /// </summary>
    public string SignPayload(string rawBody, DateTimeOffset at)
    {
        var timestamp = at.ToUnixTimeSeconds();

        return $"t={timestamp},v1={Sign(options.Value.WebhookSecret, timestamp, rawBody)}";
    }

    /// <summary>The clock the shop runs on, so a caller can sign "now" without
    /// reaching for <c>DateTimeOffset.UtcNow</c> and defeating every time test.</summary>
    public DateTimeOffset Now => clock.GetUtcNow();

    private const string CaptureFailsMarker = "-nocapture";

    private const string TokenPrefix = "tok_";

    /// <summary>The tokens forgotten since the process started; see <see cref="ForgetInstrumentAsync"/>.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> forgotten = new(StringComparer.Ordinal);

    /// <summary>What each test card is kept as: the brand and the last four a wallet shows, and how long it stays in date.</summary>
    private sealed record TestCard(string Brand, string Last4, int MonthsLeft);

    private static Dictionary<string, TestCard> Cards { get; } = new(StringComparer.Ordinal)
    {
        [CardOk] = new("Visa", "4242", 36),
        [CardMastercard] = new("Mastercard", "4444", 14),
        [CardDeclined] = new("Visa", "0002", 24),
        [CardCaptureFails] = new("Visa", "0259", 24),
        [CardExpired] = new("Visa", "0341", -1),
    };

    private static bool TryReadToken(string token, out string card, out SavedInstrument expires)
    {
        // tok_<32 hex>_<card>_<yyyyMM>
        card = string.Empty;
        expires = null!;

        var parts = token[TokenPrefix.Length..].Split('_');
        if (parts.Length != 3 || parts[0].Length != 32 || parts[2].Length != 6
            || !int.TryParse(parts[2][..4], CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[2][4..], CultureInfo.InvariantCulture, out var month))
            return false;

        card = parts[1];
        expires = new SavedInstrument(token, string.Empty, string.Empty, month, year);
        return true;
    }

    private static WebhookVerification Interpret(string rawBody)
    {
        PaymentWebhookBody? body;

        try
        {
            body = JsonSerializer.Deserialize<PaymentWebhookBody>(rawBody, Json);
        }
        catch (JsonException exception)
        {
            // Signed and unreadable is still not an attack: the secret held, so
            // whoever sent it is who they claim to be and merely sent nonsense.
            return WebhookVerification.Rejected(WebhookVerdict.Ignored, exception.Message);
        }

        if (body?.Kind is null || body.Reference is null)
            return WebhookVerification.Rejected(WebhookVerdict.Ignored, "The payload names no event.");

        var orderId = Guid.TryParse(body.OrderId, out var parsed) ? new OrderId(parsed) : (OrderId?)null;

        return WebhookVerification.Accepted(new PaymentEvent(body.Kind, body.Reference, orderId, body.Reason));
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record PaymentWebhookBody(string? Kind, string? Reference, string? OrderId, string? Reason);

    private static bool TryParse(string header, out long timestamp, out string signature)
    {
        timestamp = 0;
        signature = string.Empty;

        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;

            var name = part[..separator];
            var value = part[(separator + 1)..];

            if (name == "t" && long.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
                timestamp = parsed;
            else if (name == "v1")
                signature = value;
        }

        return timestamp > 0 && signature.Length > 0;
    }

    private static string Sign(string secret, long timestamp, string rawBody) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}")));

    /// <summary>
    /// A stable reference derived from what produced it. Deterministic on
    /// purpose: it is what makes retrying with the same idempotency key return
    /// the same authorisation instead of a second hold.
    /// </summary>
    private static string Reference(string prefix, string seed, bool marked = false)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..24];

        // The instrument is long gone by capture time, so the failing card
        // leaves its mark on the authorisation reference instead; see
        // CaptureAsync.
        return $"{prefix}_{digest}{(marked ? CaptureFailsMarker : string.Empty)}";
    }
}

internal sealed class KeyedPaymentProviderRegistry(IServiceProvider services) : IPaymentProviderRegistry
{
    public IReadOnlyCollection<string> Keys =>
        [.. services.GetKeyedServices<IPaymentProvider>(KeyedService.AnyKey)
            .Select(provider => provider.Key)
            .Order(StringComparer.Ordinal)];

    public IPaymentProvider Get(string key) =>
        services.GetKeyedService<IPaymentProvider>(key)
        ?? throw new UnknownPaymentProviderException(key, Keys);
}

public sealed class UnknownPaymentProviderException(string key, IReadOnlyCollection<string> known)
    : InvalidOperationException($"There is no payment provider '{key}'. Known: {string.Join(", ", known)}.")
{
    public string Key { get; } = key;
}
