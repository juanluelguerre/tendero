using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Ucp.Capabilities;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// What the shop declares it can do, and the two ways that declaration can lie.
///
/// It can lie by naming a slice that cannot run, and it can lie by naming a
/// capability nobody can reach. The first is a compile error already — a
/// capability holds `typeof(SearchProductsQuery)` — and these add the half the
/// compiler cannot see.
/// </summary>
public sealed class UcpCapabilityTests
{
    [Fact]
    public void The_shop_declares_exactly_these_capabilities()
    {
        // The exact list, so a third capability is a deliberate act rather than a
        // drift — the same assertion the WebMCP tool set is held to, and for the
        // same reason: this list IS the contract an agent reads.
        Assert.Equal(
            [
                "com.elguerre.tendero.knowledge",
                "dev.ucp.common.payment.ap2_mandate",
                "dev.ucp.shopping.catalog.lookup", "dev.ucp.shopping.catalog.search",
                "dev.ucp.shopping.checkout", "dev.ucp.shopping.discount", "dev.ucp.shopping.fulfillment",
            ],
            UcpCapabilities.Declared.Select(capability => capability.Id).ToArray(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// **A capability's schema is an address a platform fetches**, and the
    /// protocol's own are published under the version: the specification's
    /// examples write `https://ucp.dev/2026-08-25/schemas/shopping/checkout.json`.
    /// The shop declared `https://ucp.dev/schemas/…`, which is the `$id` the
    /// schemas carry in the protocol's source tree and answers 404 — found on
    /// 2026-10-13 by asking for it, after a year in which every test read the
    /// vendored copy and none read the address.
    ///
    /// What can be held here without a network: the address is under the
    /// version the shop declares, and the rest of it names a schema the vendored
    /// release really has.
    /// </summary>
    [Fact]
    public void The_protocols_own_capabilities_point_at_the_schemas_published_for_the_version_declared()
    {
        var published = $"https://ucp.dev/{UcpSpecification.Version}/schemas/";
        var own = UcpCapabilities.Declared.Where(capability => capability.Id.StartsWith("dev.ucp.", StringComparison.Ordinal)).ToList();

        Assert.Equal(6, own.Count);
        Assert.All(own, capability =>
        {
            Assert.StartsWith(published, capability.Schema, StringComparison.Ordinal);
            Assert.True(
                File.Exists(Path.Combine(AppContext.BaseDirectory, "ucp-schemas", capability.Schema[published.Length..])),
                $"{capability.Id} points at {capability.Schema}, which the vendored release does not have.");
        });
    }

    /// <summary>
    /// Ordered by id, because a manifest whose keys move between runs makes a
    /// snapshot test a coin toss.
    /// </summary>
    [Fact]
    public void They_come_back_in_a_stable_order()
    {
        var ids = UcpCapabilities.Declared.Select(capability => capability.Id).ToArray();

        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
    }

    /// <summary>
    /// A capability may only be served by something the dispatcher can actually
    /// run. Naming a plain record here would compile and would advertise a verb
    /// with nothing behind it.
    /// </summary>
    [Fact]
    public void Every_slice_a_capability_names_is_a_query_or_a_command()
    {
        var wrong = UcpCapabilities.Declared
            .SelectMany(capability => capability.Slices
                .Where(slice => !IsDispatchable(slice))
                .Select(slice => $"{capability.Id} -> {slice.FullName}"))
            .Order()
            .ToArray();

        Assert.True(wrong.Length == 0,
            "These capabilities name something the dispatcher cannot run: "
            + string.Join(", ", wrong));
    }

    [Fact]
    public void Every_capability_is_the_specifications_namespace_or_ours()
    {
        var strange = UcpCapabilities.Declared
            .Where(capability => !capability.Id.StartsWith("dev.ucp.", StringComparison.Ordinal)
                                 && !capability.Id.StartsWith("com.elguerre.tendero.", StringComparison.Ordinal))
            .Select(capability => capability.Id)
            .ToArray();

        Assert.True(strange.Length == 0,
            "A capability id is either the specification's or a vendor extension of ours: "
            + string.Join(", ", strange));
    }

    /// <summary>
    /// And what is still absent, deliberately. Checkout, fulfillment and
    /// discount arrived in phase 11 (P11-1), once an agent had a principal and
    /// both bindings existed, and the AP2 mandate once its verifier did (P11-4).
    /// **Order** waits because declaring it commits the shop to signed webhooks;
    /// **cart** because a checkout is created from line items and nothing yet needs a
    /// cart of its own over UCP. This test is what keeps each of them a deliberate
    /// act rather than a class added.
    /// </summary>
    [Theory]
    [InlineData("dev.ucp.shopping.cart")]
    [InlineData("dev.ucp.shopping.order")]
    [InlineData("dev.ucp.common.identity_linking")]
    public void What_is_not_built_is_not_declared(string id) =>
        Assert.DoesNotContain(id, UcpCapabilities.Declared.Select(capability => capability.Id));

    /// <summary>
    /// Discovery, not registration: a capability that exists is found. Asserted
    /// against a type this test names, so the mechanism is what is being read
    /// and not the two classes that happen to exist.
    /// </summary>
    [Fact]
    public void A_capability_is_found_because_it_exists_and_not_because_it_was_listed()
    {
        var found = UcpCapabilities.DiscoverIn(typeof(CatalogSearchCapability).Assembly);

        Assert.Contains(found, capability => capability is CatalogSearchCapability);
        Assert.Contains(found, capability => capability is CatalogLookupCapability);
    }

    private static bool IsDispatchable(Type slice) =>
        slice.GetInterfaces().Any(contract =>
            contract.IsGenericType
            && (contract.GetGenericTypeDefinition() == typeof(IQuery<>)
                || contract.GetGenericTypeDefinition() == typeof(ICommand<>)));
}
