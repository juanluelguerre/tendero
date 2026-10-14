using ElGuerre.Tendero.Ucp;
using ElGuerre.Tendero.Ucp.Capabilities;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// UCP's **authority binding** (the specification, *Authority Binding*): a
/// capability's `schema` URL has to be on a host whose labels, reversed, are the
/// capability's name or a label-aligned prefix of it — and a platform *MUST*
/// reject one that is not, treating it as not declared at all.
///
/// The shop's own extension is `com.elguerre.tendero.knowledge`, and until
/// 2026-10-13 its schema was declared wherever the shop happened to be served,
/// so a conformant platform dropped it without a word — on `localhost` always,
/// since a single-label host is no authority. Nothing tested it, because the
/// only platform that had ever negotiated it was one this repository wrote.
/// </summary>
public sealed class UcpAuthorityTests
{
    /// <summary>The specification's own table, row for row.</summary>
    [Theory]
    [InlineData("dev.ucp.shopping.checkout", "https://ucp.dev/schemas/checkout.json", true)]
    [InlineData("dev.ucp.shopping.checkout", "https://shopping.ucp.dev/checkout.json", true)]
    [InlineData("com.example.payments.installments", "https://example.com/i.json", true)]
    [InlineData("com.example.pay", "https://pay.example.com/schema.json", true)]
    [InlineData("com.example.pay", "https://example.com/schema.json", true)]
    [InlineData("com.example.pay", "https://evil.example/schema.json", false)]
    [InlineData("dev.ucp.shopping.checkout", "https://evil.example/checkout.json", false)]
    [InlineData("com.examplecorp.pay", "https://example.com/schema.json", false)]
    [InlineData("com.example.pay", "https://cdn.example.com/schema.json", false)]
    public void A_schema_binds_from_a_host_its_name_reverses_to(string name, string schema, bool binds) =>
        Assert.Equal(binds, UcpAuthority.Binds(name, schema));

    /// <summary>
    /// What the derivation refuses before it compares anything: not `https`,
    /// credentials in the URL (whose host is the part after the `@`), an address
    /// or a single label for a host, and text that is no URL. A trailing dot and
    /// capitals are the same host; a port is not part of it.
    /// </summary>
    [Theory]
    [InlineData("http://example.com/schema.json", false)]
    [InlineData("https://example.com@evil.example/schema.json", false)]
    [InlineData("https://user:pass@example.com/schema.json", false)]
    [InlineData("https://203.0.113.10/schema.json", false)]
    [InlineData("https://[2001:db8::1]/schema.json", false)]
    [InlineData("https://localhost/schema.json", false)]
    [InlineData("/ucp/schemas/com.example.pay.json", false)]
    [InlineData("not a url", false)]
    [InlineData("https://EXAMPLE.com./schema.json", true)]
    [InlineData("https://example.com:8443/schema.json", true)]
    public void A_schema_url_that_names_no_authority_binds_nothing(string schema, bool binds) =>
        Assert.Equal(binds, UcpAuthority.Binds("com.example.pay", schema));

    /// <summary>
    /// A single label is no authority even for a name that starts with it. The
    /// row above asks `localhost` about `com.example.pay`, which it could not
    /// bind anyway, so the mutant that let one label through survived Stryker
    /// (phase 13b's close: `||` read as `&amp;&amp;`).
    /// </summary>
    [Fact]
    public void A_single_label_host_binds_not_even_a_name_that_starts_with_it() =>
        Assert.False(UcpAuthority.Binds("localhost.tendero.kits", "https://localhost/schema.json"));

    /// <summary>
    /// **The profile the shop publishes**, told where its own schemas are
    /// (`Ucp:OwnSchemasUrl`): every capability it declares is bound, the
    /// specification's to `ucp.dev` and its own to a host under `elguerre.com`.
    /// </summary>
    [Fact]
    public void Every_capability_the_shop_declares_is_bound_to_its_namespace()
    {
        var profile = UcpProfileEndpoint.Compose("https://shop.example", ownSchemas: "https://tendero.elguerre.com");

        var unbound = profile.Ucp.Capabilities
            .SelectMany(entry => entry.Value.Select(declared => (Name: entry.Key, declared.Schema)))
            .Where(capability => !UcpAuthority.Binds(capability.Name, capability.Schema))
            .ToArray();

        Assert.Empty(unbound);
        Assert.Equal(
            "https://tendero.elguerre.com/ucp/schemas/com.elguerre.tendero.knowledge.json",
            profile.Ucp.Capabilities[KnowledgeCapability.Name].Single().Schema);
    }

    /// <summary>
    /// And told nothing, it names the host it is served on — which binds the
    /// shop's own extension only when that host is under its namespace. Written
    /// down as a test so that the gap is a fact with a name and not a surprise:
    /// a shop on any other host has to set the option, and one on `localhost`
    /// cannot offer the extension to a platform that checks.
    /// </summary>
    [Theory]
    [InlineData("https://tendero.elguerre.com", true)]
    [InlineData("https://shop.example", false)]
    [InlineData("http://localhost:5080", false)]
    public void Without_the_setting_the_shops_own_extension_binds_only_from_its_own_domain(string servedAt, bool binds)
    {
        var knowledge = UcpProfileEndpoint.Compose(servedAt).Ucp.Capabilities[KnowledgeCapability.Name].Single();

        Assert.Equal(binds, UcpAuthority.Binds(KnowledgeCapability.Name, knowledge.Schema));
    }

    /// <summary>A setting that could not bind the shop's own names is refused when the shop starts, not found by a platform.</summary>
    [Theory]
    [InlineData("https://elguerre.com", true)]
    [InlineData("https://tendero.elguerre.com/", true)]
    [InlineData("https://cdn.elguerre.com", false)]
    [InlineData("http://elguerre.com", false)]
    [InlineData("https://schemas.example", false)]
    public void Where_the_shops_own_schemas_are_has_to_bind_them(string ownSchemas, bool accepted) =>
        Assert.Equal(accepted, UcpAuthority.BindsOwn(ownSchemas));
}
