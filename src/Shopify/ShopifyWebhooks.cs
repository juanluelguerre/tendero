using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Carter;
using ElGuerre.Tendero.Ordering.Features.ReceiveChannelOrders;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// Whether a webhook is Shopify's (phase 16, S3; ADR 0070): the base64 HMAC-SHA256 of the
/// raw body, keyed by the app's client secret, in <c>X-Shopify-Hmac-SHA256</c>, compared in
/// constant time. Over the bytes that were sent — a body parsed and written again is not
/// what was signed, and checking it would pass forgeries.
/// </summary>
internal static class ShopifyWebhookSignature
{
    public const string Header = "X-Shopify-Hmac-SHA256";

    public static bool IsValid(string secret, byte[] body, string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        Span<byte> sent = stackalloc byte[64];
        if (!Convert.TryFromBase64String(signature, sent, out var length))
            return false;

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(expected, sent[..length]);
    }
}

/// <summary>
/// The store's webhooks (phase 16, S3; ADR 0070). A new order is a notice, not a
/// payload to map: its id is read and the order fetched through the same GraphQL and
/// mapping the reconciliation uses, so the REST shape of a webhook body is never a
/// second mapping to keep. Shopify gives five seconds and retries eight times over four
/// hours, so a topic Tendero does not handle answers 200 and is not retried into the
/// subscription's deletion. A duplicate delivery is one order, by the order's key.
/// </summary>
public sealed class ShopifyWebhookEndpoint : ICarterModule
{
    public const string Path = "/api/channels/shopify/webhooks";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(Path,
            async Task<Results<Ok<string>, UnauthorizedHttpResult, NotFound>> (
                HttpContext http, IOptions<ShopifyOptions> options, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                // No store connected, no webhook: the shop runs as it always has.
                if ((options.Value.WebhookSecret ?? options.Value.ClientSecret) is not { Length: > 0 } secret)
                    return TypedResults.NotFound();

                using var buffer = new MemoryStream();
                await http.Request.Body.CopyToAsync(buffer, ct);
                var body = buffer.ToArray();

                if (!ShopifyWebhookSignature.IsValid(secret, body, http.Request.Headers[ShopifyWebhookSignature.Header]))
                    return TypedResults.Unauthorized();

                if (http.Request.Headers["X-Shopify-Topic"] != "orders/create")
                    return TypedResults.Ok("ignored");

                var order = JsonDocument.Parse(body).RootElement;
                var id = order.TryGetProperty("admin_graphql_api_id", out var gid) ? gid.GetString() : order.GetProperty("id").GetRawText();
                var received = await dispatcher.SendAsync(new ReceiveChannelOrderCommand(ShopifySalesChannel.Key, id!), ct);

                return TypedResults.Ok(received.Received > 0 ? "received" : received.AlreadyHere > 0 ? "already here" : "skipped");
            })
            // Anonymous BY NECESSITY, as the payment provider's webhook is: the caller is
            // Shopify's server and has no token of ours. The signature is the authentication.
            .AllowAnonymous()
            .DisableAntiforgery()
            .WithTags("Channels")
            .WithName("ShopifyWebhook");
    }
}
