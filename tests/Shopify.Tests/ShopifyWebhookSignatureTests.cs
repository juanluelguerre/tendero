using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace ElGuerre.Tendero.Shopify.Tests;

/// <summary>
/// Whether a webhook is the store's (phase 16, S3): the base64 HMAC-SHA256 of the raw
/// body with the app's secret — and nothing else passes, not a body changed by a byte,
/// not another app's secret, not a header that is missing or not base64.
/// </summary>
public sealed class ShopifyWebhookSignatureTests
{
    private const string Secret = "shpss_lab_secret";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"id":7001,"admin_graphql_api_id":"gid://shopify/Order/7001"}""");

    private static string Signed(byte[] body, string secret = Secret) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    [Fact]
    public void The_stores_signature_over_the_raw_body_passes() =>
        Assert.True(ShopifyWebhookSignature.IsValid(Secret, Body, Signed(Body)));

    [Fact]
    public void A_body_changed_by_one_byte_does_not()
    {
        var tampered = (byte[])Body.Clone();
        tampered[^2] ^= 1;

        Assert.False(ShopifyWebhookSignature.IsValid(Secret, tampered, Signed(Body)));
    }

    [Fact]
    public void Another_apps_secret_does_not() =>
        Assert.False(ShopifyWebhookSignature.IsValid(Secret, Body, Signed(Body, "another-secret")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("c2hvcnQ=")]
    public void A_missing_malformed_or_short_signature_does_not(string? signature) =>
        Assert.False(ShopifyWebhookSignature.IsValid(Secret, Body, signature));
}
