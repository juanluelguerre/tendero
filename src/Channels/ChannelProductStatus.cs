namespace ElGuerre.Tendero.Channels;

// In the context's root namespace, not Ports or Domain: the domain records it and
// the port carries it, and the domain may not depend on its own Ports (the rule
// Domain_code_depends_on_the_shared_kernel_and_nothing_else) nor an adapter on a domain.
/// <summary>Where a product stands on a channel. A closed set: a new one does not compile until each channel maps it.</summary>
public enum ChannelProductStatus
{
    /// <summary>Active in Tendero: on the channel's shelf.</summary>
    OnSale,

    /// <summary>A draft, or withdrawn: the channel holds it and does not sell it (ADR 0012 on every channel).</summary>
    NotOnSale,

    /// <summary>Gone from the catalogue: archived on the channel too.</summary>
    Archived,
}
