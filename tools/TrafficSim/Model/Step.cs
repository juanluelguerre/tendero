namespace ElGuerre.Tendero.TrafficSim.Model;

/// <summary>
/// The states of the Customer Behavior Model Graph (Menascé): the kinds of page
/// a person is on, and the kinds of call an agent makes. A journey is a walk over
/// them; a driver turns each one into clicks or requests.
/// </summary>
public enum Step
{
    // A person, in the storefront.
    Home,
    Search,
    Browse,
    Product,
    AddToCart,
    Cart,
    Checkout,

    /// <summary>Pressing *Pay*: the only step that places an order. Reaching
    /// <see cref="Checkout"/> and leaving is the abandonment Baymard measures.</summary>
    Pay,

    // An agent, over MCP.
    McpSearch,
    McpProduct,

    // An agent, over UCP's REST binding.
    UcpSearch,
    UcpLookup,

    Exit
}
