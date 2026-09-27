using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Tests;
using ElGuerre.Tendero.Ucp.Ap2;
using ElGuerre.Tendero.Ucp.Negotiation;
using ElGuerre.Tendero.Ucp.Signing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// AP2's checkout mandate (P11-4), from both ends: the shop signs a checkout, a
/// platform signs a mandate over it, and the shop verifies the mandate — and every
/// way it can fail is exactly one of UCP's error codes.
/// </summary>
public sealed class Ap2MandateTests : IDisposable
{
    private const string Agent = "claude-desktop-test";
    private const string Audience = "https://shop.example";

    private readonly TestClock clock = new();
    private readonly TestMerchant merchant = new();
    private readonly DevelopmentPlatforms platforms;
    private readonly ServiceProvider services;

    public Ap2MandateTests()
    {
        platforms = new DevelopmentPlatforms(clock);
        services = new ServiceCollection()
            .AddSingleton<IMerchantSigner>(merchant)
            .AddKeyedSingleton<ICheckoutMandateVerifier, ClosedCheckoutMandateVerifier>(ClosedCheckoutMandateVerifier.Vct)
            .BuildServiceProvider();
    }

    private CheckoutMandates Mandates => new(services, clock);

    private PlatformProfile Platform => new(new Dictionary<string, IReadOnlySet<string>>()) { Keys = [platforms.KeyOf(Agent)] };

    internal static JsonObject Checkout(int quantity = 2, string title = "Zapatillas de running") => new()
    {
        ["ucp"] = new JsonObject { ["version"] = UcpSpecification.Version },
        ["id"] = "0199a1f0-0000-7000-8000-00000000c0de",
        ["status"] = "ready_for_complete",
        ["currency"] = "EUR",
        ["line_items"] = new JsonArray(new JsonObject
        {
            ["id"] = "B073WXYZ01-38",
            ["item"] = new JsonObject { ["id"] = "B073WXYZ01-38", ["title"] = title, ["price"] = 7995 },
            ["quantity"] = quantity,
            ["totals"] = new JsonArray(new JsonObject { ["type"] = "total", ["amount"] = 7995 * quantity }),
        }),
        ["totals"] = new JsonArray(new JsonObject { ["type"] = "total", ["amount"] = 7995 * quantity }),
    };

    private string Mandate(JsonObject? checkout = null, TimeSpan? lifetime = null) =>
        platforms.SignCheckoutMandate(Agent, MerchantAuthorization.Sign(checkout ?? Checkout(), merchant), merchant.PublishedKeys, Audience, lifetime)!;

    [Fact]
    public void A_mandate_over_a_checkout_the_shop_signed_verifies_and_carries_that_checkout()
    {
        var verdict = Mandates.Verify(Mandate(), Platform);

        Assert.Null(verdict.Error);
        Assert.Equal(ClosedCheckoutMandateVerifier.Vct, verdict.Mandate!.Vct);
        Assert.Equal(platforms.KeyOf(Agent).KeyId, verdict.Mandate.IssuerKeyId);
        Assert.Equal(clock.GetUtcNow().AddMinutes(15).ToUnixTimeSeconds(), verdict.Mandate.ExpiresAt!.Value.ToUnixTimeSeconds());
        Assert.True(UcpCheckoutTerms.Same(verdict.Mandate.Checkout, Checkout()));
    }

    /// <summary>The signature covers the checkout without its <c>ap2</c> member, all of it: change anything else and it no longer verifies.</summary>
    [Fact]
    public void The_shops_signature_covers_every_member_but_ap2()
    {
        var signed = MerchantAuthorization.Sign(Checkout(), merchant);
        Assert.True(MerchantAuthorization.Verify(signed, merchant.PublishedKeys));

        signed["ap2"]!["something_later"] = "ignored";
        Assert.True(MerchantAuthorization.Verify(signed, merchant.PublishedKeys));

        signed["totals"]![0]!["amount"] = 1;
        Assert.False(MerchantAuthorization.Verify(signed, merchant.PublishedKeys));
        Assert.False(MerchantAuthorization.Verify(Checkout(), merchant.PublishedKeys));
    }

    [Fact]
    public void No_mandate_is_mandate_required_and_an_unknown_key_is_agent_missing_key()
    {
        Assert.Equal(Ap2ErrorCodes.MandateRequired, Mandates.Verify(null, Platform).Error);
        Assert.Equal(Ap2ErrorCodes.MandateRequired, Mandates.Verify(" ", Platform).Error);
        Assert.Equal(Ap2ErrorCodes.AgentMissingKey, Mandates.Verify(Mandate(), Platform with { Keys = [] }).Error);
    }

    [Fact]
    public void A_mandate_that_has_expired_is_mandate_expired()
    {
        var mandate = Mandate();
        clock.Advance(DevelopmentPlatforms.DefaultLifetime);

        Assert.Equal(Ap2ErrorCodes.MandateExpired, Mandates.Verify(mandate, Platform).Error);
    }

    /// <summary>A platform checks the shop's signature before asking anyone; this one asked anyway, over a checkout another key signed.</summary>
    [Fact]
    public void A_checkout_another_key_signed_is_merchant_authorization_invalid()
    {
        var impostor = new TestMerchant();
        var mandate = platforms.SignCheckoutMandate(Agent, MerchantAuthorization.Sign(Checkout(), impostor), impostor.PublishedKeys, Audience)!;

        Assert.Null(platforms.SignCheckoutMandate(Agent, MerchantAuthorization.Sign(Checkout(), impostor), merchant.PublishedKeys, Audience));
        Assert.Equal(Ap2ErrorCodes.MerchantAuthorizationInvalid, Mandates.Verify(mandate, Platform).Error);
    }

    public static TheoryData<string, Func<string, string>> Tamperings => new()
    {
        { "the issuer's signature", mandate => Flip(mandate, mandate.IndexOf('~', StringComparison.Ordinal) - 2) },
        { "a disclosure", mandate => Flip(mandate, mandate.IndexOf('~', StringComparison.Ordinal) + 5) },
        { "the key-binding signature", mandate => Flip(mandate, mandate.Length - 2) },
        { "no key binding", mandate => mandate[..(mandate.LastIndexOf('~') + 1)] },
        { "no disclosure", mandate => mandate[..(mandate.IndexOf('~', StringComparison.Ordinal) + 1)] + mandate[(mandate.LastIndexOf('~') + 1)..] },
        { "not an SD-JWT", _ => "not.a.jwt~" },
    };

    [Theory]
    [MemberData(nameof(Tamperings))]
    public void A_mandate_changed_anywhere_is_mandate_invalid_signature(string what, Func<string, string> tamper)
    {
        var verdict = Mandates.Verify(tamper(Mandate()), Platform);

        Assert.True(verdict.Error == Ap2ErrorCodes.MandateInvalidSignature, $"{what}: {verdict.Error}");
    }

    /// <summary>
    /// Signed by the platform, and still refused: a <c>checkout_hash</c> that is not
    /// the hash of <c>checkout_jwt</c>, and a <c>vct</c> this shop does not verify.
    /// </summary>
    [Theory]
    [InlineData(ClosedCheckoutMandateVerifier.Vct, "not-the-hash")]
    [InlineData("mandate.checkout.open.1", null)]
    public void Claims_the_platform_signed_still_have_to_mean_what_they_say(string vct, string? hash)
    {
        using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuer = new EcdsaJwsSigner(issuerKey);
        var checkoutJwt = MerchantAuthorization.CheckoutJwtOf(MerchantAuthorization.Sign(Checkout(), merchant));
        var claims = new JsonObject
        {
            ["vct"] = vct,
            ["checkout_jwt"] = checkoutJwt,
            ["checkout_hash"] = hash ?? SdJwt.Digest(checkoutJwt),
        };

        var mandate = SdJwt.Issue(issuer, claims, ["checkout_jwt"], new EcdsaJwsSigner(holderKey), Audience, "n", clock.GetUtcNow());
        var platform = Platform with { Keys = [issuer.PublicKey] };

        Assert.Equal(Ap2ErrorCodes.MandateInvalidSignature, Mandates.Verify(mandate, platform).Error);
    }

    [Fact]
    public void Terms_are_the_goods_and_the_amounts_not_the_words()
    {
        Assert.True(UcpCheckoutTerms.Same(Checkout(), Checkout(title: "Running shoes")));
        Assert.False(UcpCheckoutTerms.Same(Checkout(), Checkout(quantity: 3)));

        // Three for the price of two — a line discount — is the same money and not
        // the same purchase: the quantity is compared on its own.
        var threeForTwo = Checkout();
        threeForTwo["line_items"]![0]!["quantity"] = 3;
        Assert.False(UcpCheckoutTerms.Same(Checkout(), threeForTwo));

        var other = Checkout();
        other["id"] = "another-session";
        Assert.False(UcpCheckoutTerms.Same(Checkout(), other));

        var dollars = Checkout();
        dollars["currency"] = "USD";
        Assert.False(UcpCheckoutTerms.Same(Checkout(), dollars));
    }

    [Fact]
    public void A_presentation_reads_back_what_was_issued_and_rejects_a_disclosure_nothing_refers_to()
    {
        using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var claims = new JsonObject { ["vct"] = "x", ["secret"] = new JsonObject { ["a"] = 1 }, ["plain"] = true };

        var issued = SdJwt.Issue(new EcdsaJwsSigner(issuerKey), claims, ["secret"], new EcdsaJwsSigner(holderKey), Audience, "n", clock.GetUtcNow());
        var read = SdJwt.Read(issued)!;

        Assert.Equal(1, read.Claims["secret"]!["a"]!.GetValue<int>());
        Assert.True(read.Claims["plain"]!.GetValue<bool>());
        Assert.True(read.KeyBindingCoversIt());

        var stray = System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes("""["salt","extra",1]"""));
        var bound = issued[..(issued.LastIndexOf('~') + 1)];
        Assert.Null(SdJwt.Read(bound + stray + "~" + issued[(issued.LastIndexOf('~') + 1)..]));
    }

    public void Dispose()
    {
        services.Dispose();
        platforms.Dispose();
        merchant.Dispose();
    }

    private static string Flip(string text, int at) =>
        text[..at] + (text[at] == 'A' ? 'B' : 'A') + text[(at + 1)..];

    internal sealed class TestMerchant : IMerchantSigner, IDisposable
    {
        private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly EcdsaJwsSigner signer;

        public TestMerchant()
        {
            signer = new EcdsaJwsSigner(key);
            PublishedKeys = [signer.PublicKey];
        }

        public string KeyId => signer.KeyId;

        public string Algorithm => signer.Algorithm;

        public IReadOnlyList<Jwk> PublishedKeys { get; }

        public byte[] Sign(ReadOnlySpan<byte> signingInput) => signer.Sign(signingInput);

        public void Dispose() => key.Dispose();
    }
}
