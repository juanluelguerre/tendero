using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Domain;

/// <summary>
/// The way an order came in (R-9, D-7): the storefront, an agent over MCP, or an
/// agent over UCP's REST. Recorded on the order when it is placed, from the
/// request's <see cref="Actor.Surface"/>, because afterwards nothing tells them
/// apart — an order placed over MCP and one over UCP leave the same checkout
/// session behind. Null on an order placed before it was recorded.
/// </summary>
public enum OrderChannel
{
    Web,
    Mcp,
    Ucp,
}

public static class OrderChannels
{
    /// <summary>
    /// The channel a surface names, or null for one that places no order of a
    /// shopper's (the worker's internal surface).
    /// </summary>
    public static OrderChannel? From(string surface) => surface switch
    {
        Surfaces.Web => OrderChannel.Web,
        Surfaces.Mcp => OrderChannel.Mcp,
        Surfaces.Ucp => OrderChannel.Ucp,
        _ => null,
    };
}
