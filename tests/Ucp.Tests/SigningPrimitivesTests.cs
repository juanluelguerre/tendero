using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Tests;
using ElGuerre.Tendero.Ucp.Ap2;
using ElGuerre.Tendero.Ucp.Signing;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// The edges of AP2's primitives that phase 11's Stryker run found untested: JCS's
/// layout rules at their boundaries, keys of another type or curve, a JWS that is not
/// one, SD-JWTs with and without key binding, and what the terms comparison compares.
/// </summary>
public sealed class SigningPrimitivesTests
{
    [Theory]
    [InlineData("0.5", "0.5")]
    [InlineData("0.0001", "0.0001")]
    [InlineData("100000000000000000000", "100000000000000000000")]
    [InlineData("1e-7", "1e-7")]
    [InlineData("-1.5", "-1.5")]
    public void Numbers_on_each_side_of_ecmascripts_layout_boundaries(string json, string expected) =>
        Assert.Equal(expected, Jcs.Number(double.Parse(json, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void A_space_is_written_as_itself_and_a_control_character_escaped() =>
        Assert.Equal("\"a b\\u001f\"", Encoding.UTF8.GetString(Jcs.Canonicalize(JsonDocument.Parse("\"a b\\u001f\"").RootElement)));

    [Fact]
    public void A_key_this_shop_cannot_use_is_skipped_not_an_error()
    {
        Assert.Null(new Jwk { KeyId = "k", KeyType = "OKP", Curve = "Ed25519", X = "AA" }.ToEcdsa());
        Assert.Null(new Jwk { KeyId = "k", KeyType = "EC", Curve = "P-256", X = "AA" }.ToEcdsa());
        Assert.Null(new Jwk { KeyId = "k", KeyType = "EC", Curve = "secp256k1", X = "AA", Y = "AA" }.ToEcdsa());
        Assert.Null(new Jwk { KeyId = "k", KeyType = "EC", Curve = "P-256", X = "AA", Y = "AA" }.ToEcdsa());
    }

    [Fact]
    public void A_p384_key_round_trips_and_signs_as_es384()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var signer = new EcdsaJwsSigner(key, "mine");
        var jwk = signer.PublicKey;

        Assert.Equal(("mine", "P-384", "ES384", "sig"), (signer.KeyId, jwk.Curve, jwk.Algorithm, jwk.Use));
        using var back = jwk.ToEcdsa()!;
        Assert.True(Jws.Verify(Jws.Read(Jws.Sign(signer, "{}"u8))!, back));
    }

    [Theory]
    [InlineData("..sig")]
    [InlineData("header..")]
    [InlineData("a.b")]
    [InlineData("!!!.e30.sig")]
    [InlineData(null)]
    public void What_is_not_a_compact_jws_reads_as_nothing(string? text) =>
        Assert.Null(Jws.Read(text));

    [Fact]
    public void A_type_goes_in_the_header_only_when_given()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signer = new EcdsaJwsSigner(key);

        Assert.Equal("kb+sd-jwt", Jws.Read(Jws.Sign(signer, "{}"u8, "kb+sd-jwt"))!.Header.GetProperty("typ").GetString());
        Assert.False(Jws.Read(Jws.Sign(signer, "{}"u8))!.Header.TryGetProperty("typ", out _));
    }

    [Fact]
    public void Only_the_named_claims_are_disclosed_and_the_digests_never_reach_the_claims()
    {
        using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issued = SdJwt.Issue(new EcdsaJwsSigner(issuerKey), new JsonObject { ["secret"] = 1, ["plain"] = 2 }, ["secret"],
            new EcdsaJwsSigner(holderKey), "https://shop.example", "n", new TestClock().GetUtcNow());

        // The issuer JWT, one disclosure, the key-binding JWT.
        Assert.Equal(3, issued.Split('~').Length);
        var read = SdJwt.Read(issued)!;
        Assert.Equal(2, JsonNode.Parse(read.Issuer.Payload)!["plain"]!.GetValue<int>());
        Assert.Null(JsonNode.Parse(read.Issuer.Payload)!["secret"]);
        Assert.Null(read.Claims["_sd"]);
        Assert.Null(read.Claims["_sd_alg"]);
    }

    [Fact]
    public void A_presentation_without_key_binding_reads_and_is_not_covered_by_one()
    {
        using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issued = SdJwt.Issue(new EcdsaJwsSigner(issuerKey), new JsonObject { ["secret"] = 1 }, ["secret"],
            new EcdsaJwsSigner(holderKey), "https://shop.example", "n", new TestClock().GetUtcNow());
        var withoutBinding = issued[..(issued.LastIndexOf('~') + 1)];
        var issuerOnly = issued[..issued.IndexOf('~', StringComparison.Ordinal)];

        var read = SdJwt.Read(withoutBinding)!;
        Assert.Null(read.KeyBinding);
        Assert.False(read.KeyBindingCoversIt());
        Assert.Equal(1, read.Claims["secret"]!.GetValue<int>());
        Assert.NotNull(SdJwt.Read(issuerOnly));
    }

    [Fact]
    public void A_key_binding_over_other_disclosures_does_not_cover_this_presentation()
    {
        using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issued = SdJwt.Issue(new EcdsaJwsSigner(issuerKey), new JsonObject { ["a"] = 1, ["b"] = 2 }, ["a", "b"],
            new EcdsaJwsSigner(holderKey), "https://shop.example", "n", new TestClock().GetUtcNow());
        var parts = issued.Split('~');
        var oneDropped = string.Join('~', parts[0], parts[1], parts[3]);

        Assert.True(SdJwt.Read(issued)!.KeyBindingCoversIt());
        Assert.False(SdJwt.Read(oneDropped)!.KeyBindingCoversIt());
    }

    [Theory]
    [InlineData("id", "another-line")]
    [InlineData("item.id", "another-sku")]
    [InlineData("item.price", 1)]
    public void Terms_compare_the_line_the_item_and_its_unit_price(string member, object value)
    {
        var changed = Ap2MandateTests.Checkout();
        var line = changed["line_items"]![0]!.AsObject();
        if (member == "id")
            line["id"] = (string)value;
        else
            line["item"]![member[5..]] = JsonValue.Create(value);

        Assert.False(UcpCheckoutTerms.Same(Ap2MandateTests.Checkout(), changed));
    }
}
