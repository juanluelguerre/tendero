namespace ElGuerre.Tendero.Ucp;

/// <summary>
/// One verb this shop offers an agent, declared in code so the manifest cannot
/// drift from it.
///
/// **The point of the interface is <see cref="Slices"/>.** A capability names
/// the slices that serve it by holding their query and command TYPES, not their
/// names as strings — so deleting `SearchProductsQuery` does not produce a
/// manifest that quietly advertises a search that no longer exists; it produces
/// a build error in this project. Hand-written JSON at a well-known path rots
/// the first time an endpoint changes and nothing catches it, which is the
/// failure this design exists to make impossible.
///
/// A capability is discovered rather than registered: <see cref="UcpCapabilities"/>
/// scans for implementations, so adding one is adding a class.
/// </summary>
public interface IUcpCapability
{
    /// <summary>
    /// The reverse-DNS identifier the specification defines —
    /// <c>dev.ucp.shopping.catalog.search</c> — or <c>com.elguerre.tendero.*</c>
    /// for an extension of our own.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The capability's version, a date. It is separate from the protocol's own
    /// version because a capability may settle while the specification moves.
    /// </summary>
    string Version { get; }

    /// <summary>Where a human reads what this capability means.</summary>
    string Spec { get; }

    /// <summary>
    /// The query and command types that serve it.
    ///
    /// Types rather than names, so the compiler is what keeps the manifest
    /// honest. A test asserts each one is really an <c>IQuery&lt;&gt;</c> or an
    /// <c>ICommand&lt;&gt;</c>: a capability may only be served by something the
    /// dispatcher can actually run.
    /// </summary>
    IReadOnlyList<Type> Slices { get; }
}
