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
            ["dev.ucp.shopping.catalog.lookup", "dev.ucp.shopping.catalog.search"],
            UcpCapabilities.Declared.Select(capability => capability.Id).ToArray(),
            StringComparer.Ordinal);
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
    /// And the transactional half is absent, deliberately.
    ///
    /// Every slice behind cart, discount, fulfillment and checkout exists and
    /// has since phase 5. What does not exist is a way for an agent to be
    /// authorised — its own principal and a signed mandate are phase 11 — so
    /// declaring them would advertise a checkout no agent can reach. This test
    /// is what makes phase 11 a deliberate act rather than something that
    /// happens by adding a class.
    /// </summary>
    [Theory]
    [InlineData("dev.ucp.shopping.cart")]
    [InlineData("dev.ucp.shopping.discount")]
    [InlineData("dev.ucp.shopping.fulfillment")]
    [InlineData("dev.ucp.shopping.checkout")]
    [InlineData("dev.ucp.shopping.orders")]
    [InlineData("dev.ucp.common.identity_linking")]
    public void The_transactional_capabilities_are_not_declared_yet(string id) =>
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
