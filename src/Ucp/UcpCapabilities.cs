using System.Reflection;

namespace ElGuerre.Tendero.Ucp;

/// <summary>
/// Every capability this shop declares, found rather than listed.
///
/// **Why scanning, when the implementations all live in this one assembly.** It
/// is not ceremony around a static array: it means adding a capability is adding
/// a class, and forgetting to register it is not a failure mode that exists.
/// The mechanism is the one <c>AddTenderoCqrs</c> already uses for handlers, and
/// the reason is the same — a list somebody has to remember to update is a
/// list that goes stale.
///
/// **Two capabilities, and the transactional ones are absent on purpose.** The
/// slices behind cart, discount, fulfillment and checkout all exist and have
/// since phase 5. What does not exist is any way for an agent to be
/// authorised — its own principal and a signed mandate are phase 11 — so
/// declaring `dev.ucp.shopping.checkout` today would advertise a checkout no
/// agent can reach. It is the same rule that keeps `payment_handlers` out of the
/// profile: a manifest is a promise, and an unkeepable promise is worse than
/// silence.
/// </summary>
public static class UcpCapabilities
{
    /// <summary>
    /// Discovered from the given assemblies, ordered by id so the manifest is
    /// byte-stable across runs — which is what makes a snapshot test of the
    /// served document meaningful rather than flaky.
    /// </summary>
    public static IReadOnlyList<IUcpCapability> DiscoverIn(params Assembly[] assemblies) =>
        [.. assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                           && typeof(IUcpCapability).IsAssignableFrom(type))
            .Select(Activator.CreateInstance)
            .OfType<IUcpCapability>()
            .OrderBy(capability => capability.Id, StringComparer.Ordinal)];

    /// <summary>The ones this assembly declares, which is all of them today.</summary>
    public static IReadOnlyList<IUcpCapability> Declared =>
        DiscoverIn(typeof(UcpCapabilities).Assembly);
}
