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
            return new PaymentWebhookResult(WebhookVerdict.Ignored, "The payload names no order.");

        var order = await orders.FindByIdAsync(orderId, cancellationToken);

        if (order is null)
            return new PaymentWebhookResult(WebhookVerdict.Ignored, $"There is no order {orderId}.");

        switch (paymentEvent.Kind)
        {
            case Captured:
                // Idempotent inside the aggregate: a redelivery finds it already
                // captured and raises nothing. That check lives on the order and
                // not here because the shipping handler reaches the same method.
                order.CapturePayment(clock, paymentEvent.Reference);
                break;

            case Failed when order.Status == OrderStatus.PaymentAuthorized:
                order.FailPayment(clock, paymentEvent.Reason ?? "The provider reported a failure.");
                break;

            default:
                return new PaymentWebhookResult(WebhookVerdict.Ignored, paymentEvent.Kind);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new PaymentWebhookResult(WebhookVerdict.Accepted, paymentEvent.Kind);
    }
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
