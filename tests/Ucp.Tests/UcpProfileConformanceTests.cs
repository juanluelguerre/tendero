using System.Text.Json;
using ElGuerre.Tendero.Tests.Ucp;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// The profile this shop publishes, checked against the specification's own JSON
/// Schema rather than against what somebody read on a documentation page.
///
/// That distinction has already cost this project three mistakes, all of them
/// made confidently: the signing-key field is <c>keys</c> and not
/// <c>signing_keys</c>; a capability's registry value is an array; and a
/// business profile REQUIRES <c>payment_handlers</c>, which was omitted on the
/// reasoned-and-wrong grounds that an empty map advertises a checkout we cannot
/// honour. Prose describes a schema. Only the schema is the schema.
/// </summary>
public sealed class UcpProfileConformanceTests
{
    private const string BusinessProfile = "https://ucp.dev/schemas/profile.json#/$defs/business_schema";

    /// <summary>A key as the shop publishes it (P11-4): the profile is checked with its <c>keys</c>, not without.</summary>
    private static readonly IReadOnlyList<Signing.Jwk> SigningKeys =
        [Signing.Jwk.FromEcdsa(System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256))];

    private static JsonElement Profile
    {
        get
        {
            var json = JsonSerializer.Serialize(
                UcpProfileEndpoint.Compose("https://shop.example", SigningKeys),
                // The naming policy the rest of the API uses, on purpose: if the
                // explicit names ever stop winning, this fails as a conformance
                // error rather than as a curiosity.
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            return JsonDocument.Parse(json).RootElement.Clone();
        }
    }

    [Fact]
    public void The_profile_validates_against_the_business_schema() =>
        UcpSchemas.Validate(BusinessProfile, Profile, "The profile served at /.well-known/ucp");

    /// <summary>
    /// The guards on the guard, and they earned their place immediately: the
    /// first version of these tests reported the profile as valid AND a profile
    /// with no <c>payment_handlers</c> as valid, because the registry resolves a
    /// document by base URI and ignores the fragment. The positive assertion
    /// above was passing against <c>profile.json</c>'s root, which requires
    /// nothing but a <c>ucp</c> member.
    ///
    /// So every row here is a document that MUST be rejected, and each is a
    /// mistake this project actually made or nearly made.
    /// </summary>
    [Theory]
    [InlineData(
        "no payment_handlers — required of a business profile, and omitted here on the "
        + "reasoned-and-wrong grounds that an empty map advertises a checkout we cannot honour",
        """{"ucp":{"version":"2026-08-25","services":{},"capabilities":{},"keys":[]}}""")]
    [InlineData(
        "no version",
        """{"ucp":{"services":{},"capabilities":{},"payment_handlers":{}}}""")]
    [InlineData(
        "a capability as an object instead of an array — the shape this code had until the schema said otherwise",
        """
        {"ucp":{"version":"2026-08-25","services":{},"payment_handlers":{},
          "capabilities":{"dev.ucp.shopping.catalog.search":
            {"version":"2026-08-25","schema":"https://ucp.dev/schemas/shopping/catalog_search.json"}}}}
        """)]
    [InlineData(
        "a capability with no schema — required of a business profile, because a platform fetches it to compose the capability",
        """
        {"ucp":{"version":"2026-08-25","services":{},"payment_handlers":{},
          "capabilities":{"dev.ucp.shopping.catalog.search":[{"version":"2026-08-25"}]}}}
        """)]
    [InlineData(
        "an mcp binding with no endpoint — a service an agent cannot reach",
        """
        {"ucp":{"version":"2026-08-25","capabilities":{},"payment_handlers":{},
          "services":{"dev.ucp.shopping":[{"version":"2026-08-25","transport":"mcp"}]}}}
        """)]
    [InlineData(
        "a transport the specification does not define",
        """
        {"ucp":{"version":"2026-08-25","capabilities":{},"payment_handlers":{},
          "services":{"dev.ucp.shopping":[{"version":"2026-08-25","transport":"carrier-pigeon","endpoint":"https://shop.example/mcp"}]}}}
        """)]
    [InlineData(
        "a private key member — a profile is public, and `d` is the half that signs",
        """
        {"ucp":{"version":"2026-08-25","services":{},"capabilities":{},"payment_handlers":{}},
          "keys":[{"kid":"k1","kty":"EC","crv":"P-256","x":"AA","y":"AA","alg":"ES256","d":"AA"}]}
        """)]
    [InlineData(
        "a P-256 key named for ES384 — the pairing UCP pins",
        """
        {"ucp":{"version":"2026-08-25","services":{},"capabilities":{},"payment_handlers":{}},
          "keys":[{"kid":"k1","kty":"EC","crv":"P-256","x":"AA","y":"AA","alg":"ES384"}]}
        """)]
    public void A_profile_that_breaks_the_schema_is_rejected(string what, string json)
    {
        var instance = JsonDocument.Parse(json).RootElement;

        Assert.False(
            UcpSchemas.Get(BusinessProfile).Evaluate(instance).IsValid,
            $"A profile with {what} must not validate. If it does, the schema is not being "
            + "applied and every other assertion in this file is empty.");
    }
}
