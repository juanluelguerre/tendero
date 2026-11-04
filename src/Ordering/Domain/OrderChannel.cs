using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Domain;

/// <summary>
/// The way an order came in (R-9, D-7): the storefront, an agent over MCP, an
/// agent over UCP's REST, or a sales channel that took it (phase 16, S3: Shopify). Recorded on the order when it is placed, from the
/// request's <see cref="Actor.Surface"/>, because afterwards nothing tells them
/// apart — an order placed over MCP and one over UCP leave the same checkout
/// session behind. Null on an order placed before it was recorded.
/// </summary>
public enum OrderChannel
{
    Web,
    Mcp,
    Ucp,

    /// <summary>Taken by a Shopify store Tendero publishes to (ADR 0070); never a request's surface.</summary>
    Shopify,
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
