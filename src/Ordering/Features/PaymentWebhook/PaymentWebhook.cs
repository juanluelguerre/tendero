using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Ordering.Features.PaymentWebhook;

/// <summary>
/// What a provider tells us happened, after the fact.
///
/// A webhook is the one endpoint in the shop that an unauthenticated stranger is
/// SUPPOSED to call, which is why the signature is the whole security model: it
/// is `AllowAnonymous` by necessity, and an unsigned body is refused before
/// anything in it is read. An endpoint that trusted the payload would be a
/// public API for marking orders paid.
///
/// Three refusals, three verdicts, and they are deliberately distinguishable.
/// A bad signature is somebody forging; a stale one is somebody replaying a
/// request captured earlier; an unknown kind is neither, and logging it as an
/// attack is how a log stops being read.
/// </summary>
public sealed record PaymentWebhookCommand(
    string Provider, string RawBody, IReadOnlyDictionary<string, string> Headers)
    : ICommand<PaymentWebhookResult>;

public sealed record PaymentWebhookResult(WebhookVerdict Verdict, string? Detail);

public sealed class PaymentWebhookHandler(
    IPaymentProviderRegistry providers,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<PaymentWebhookHandler> logger)
    : ICommandHandler<PaymentWebhookCommand, PaymentWebhookResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    /// <summary>The kinds this shop acts on. Everything else is signed, fresh
    /// and none of our business — a provider sends dozens.</summary>
    public const string Captured = "payment.captured";

    public const string Failed = "payment.failed";

    public async Task<PaymentWebhookResult> HandleAsync(
        PaymentWebhookCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("payments.webhook");
        activity?.SetTag("payments.provider", command.Provider);

        var verification = providers.Get(command.Provider)
            .VerifyWebhook(command.RawBody, command.Headers, clock.GetUtcNow());

        activity?.SetTag("payments.verdict", verification.Verdict.ToString());

        if (verification.Verdict != WebhookVerdict.Accepted || verification.Event is null)
        {
            if (verification.Verdict is WebhookVerdict.BadSignature or WebhookVerdict.Stale)
                logger.LogWarning(
                    "Refused a {Provider} webhook: {Verdict} — {Detail}",
                    command.Provider, verification.Verdict, verification.Detail);

            return new PaymentWebhookResult(verification.Verdict, verification.Detail);
        }

        var paymentEvent = verification.Event;
        activity?.SetTag("payments.kind", paymentEvent.Kind);

        // The order id is the provider's word for it. It is looked up rather
        // than trusted: a signed message from the right provider can still name
        // an order that does not exist, and 200 is the right answer to that or
        // the provider retries forever.
        if (paymentEvent.OrderId is not { } orderId)
            return Ignored("The payload names no order.");

        var order = await orders.FindByIdAsync(orderId, cancellationToken);

        if (order is null)
            return Ignored($"There is no order {orderId}.");

        var result = paymentEvent.Kind switch
        {
            Captured => ApplyCapture(order, paymentEvent),
            Failed => ApplyFailure(order, paymentEvent, command.Provider),
            // A kind this shop does not act on: answered and dropped, quietly.
            _ => Ignored(paymentEvent.Kind)
        };

        // Only a message that changed the order has anything to save.
        if (result.Verdict == WebhookVerdict.Accepted)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }

    /// <summary>
    /// Idempotent inside the aggregate: a redelivery finds it already captured
    /// and raises nothing. That check lives on the order and not here because
    /// the shipping handler reaches the same method.
    /// </summary>
    private PaymentWebhookResult ApplyCapture(Order order, PaymentEvent paymentEvent)
    {
        order.CapturePayment(clock, paymentEvent.Reference);
        return Accepted(paymentEvent);
    }

    /// <summary>
    /// What a failed payment does depends on where the money and the goods are,
    /// and only one of the four answers touches the order.
    /// </summary>
    private PaymentWebhookResult ApplyFailure(Order order, PaymentEvent paymentEvent, string provider)
    {
        // After the capture, a failure contradicts a fact the order holds —
        // almost always an earlier attempt's failure delivered after a later
        // one's success — so the order is not touched. It is still reported:
        // money that really went back after a capture is a refund or a
        // chargeback, and somebody has to be able to find the hint.
        if (order.Payment?.CaptureId is { } captureId)
        {
            logger.LogWarning(
                "A {Provider} webhook says the payment failed for order {OrderId}, which was already " +
                "captured as {CaptureId}. Nothing changed; if the money went back, it is a refund or a " +
                "chargeback. Reason: {Reason}",
                provider, order.Id, captureId, paymentEvent.Reason);
            return Ignored("The payment was already captured.");
        }

        // Before the money moved, a failure CANCELS: OrderCancelled is what the
        // compensation already listens to, so the stock goes back and the hold
        // is voided. It used to call FailPayment, whose transition the table
        // only allows from Pending — and no saved order is ever Pending, because
        // checkout authorises before it places (ADR 0025). Every real failure
        // threw, answered 500, and was retried forever.
        if (order.Status is OrderStatus.PaymentAuthorized or OrderStatus.Confirmed)
        {
            order.Cancel(clock, OrderStop.PaymentDeclined, paymentEvent.Reason ?? "The provider reported a failure.");
            return Accepted(paymentEvent);
        }

        // The one the shop cannot fix on its own: the goods left and the money
        // did not (a capture that fails on shipping is left uncaptured, ADR
        // 0025). It cannot be cancelled, and a return is not the system's to
        // start — it is the customer's, per line, and its refund needs a capture
        // that never happened. It needs a person, who will usually ask for the
        // payment again before asking for the goods back; the backoffice already
        // shows shipped-and-uncaptured apart.
        if (order.Status is OrderStatus.Shipped or OrderStatus.Delivered)
        {
            logger.LogWarning(
                "A {Provider} webhook says the payment failed for order {OrderId}, which is {Status} and " +
                "was never captured: the goods left and the money did not. It needs a person. Reason: {Reason}",
                provider, order.Id, order.Status, paymentEvent.Reason);
            return Ignored("The order already left uncaptured.");
        }

        // Already cancelled: its stock and hold went back when it was, so there
        // is nothing left to stop and nobody to tell.
        return Ignored(paymentEvent.Kind);
    }

    private static PaymentWebhookResult Accepted(PaymentEvent paymentEvent) =>
        new(WebhookVerdict.Accepted, paymentEvent.Kind);

    private static PaymentWebhookResult Ignored(string detail) =>
        new(WebhookVerdict.Ignored, detail);
}

public sealed class PaymentWebhookEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/payments/{provider}/webhook",
            async Task<Results<Ok<string>, UnauthorizedHttpResult>> (
                   string provider, HttpContext http, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                // The RAW body, never a deserialised object re-serialised.
                // Verifying a signature over a round-tripped payload is the
                // classic way to build a check that passes for forgeries: the
                // bytes that were signed are the only bytes worth checking.
                http.Request.EnableBuffering();
                using var reader = new StreamReader(http.Request.Body, leaveOpen: true);
                var body = await reader.ReadToEndAsync(ct);

                var result = await dispatcher.SendAsync(
                    new PaymentWebhookCommand(
                        provider,
                        body,
                        http.Request.Headers.ToDictionary(
                            header => header.Key,
                            header => header.Value.ToString(),
                            StringComparer.OrdinalIgnoreCase)),
                    ct);

                return result.Verdict switch
                {
                    // 401 on a forgery or a replay, and it is the only refusal
                    // that gets one: everything else answers 200 so the provider
                    // stops retrying something we have deliberately ignored.
                    WebhookVerdict.BadSignature or WebhookVerdict.Stale => TypedResults.Unauthorized(),
                    _ => TypedResults.Ok(result.Detail ?? "ok")
                };
            })
            // Anonymous BY NECESSITY, and it is the only endpoint in the shop
            // for which that is true: the caller is a payment provider's server
            // and it has no token of ours. The signature is the authentication.
            .AllowAnonymous()
            .WithTags("Payments")
            .WithName("PaymentWebhook");
    }
}
