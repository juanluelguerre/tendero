namespace ElGuerre.Tendero.TrafficSim.Model;

/// <summary>
/// The declared model (ADR 0037): who arrives, when, and what they do — every
/// number with its source and its grade. **The Shop pulse shows these
/// assumptions, not a market**, and the article says so.
///
/// The transition probabilities are ours (graded <see cref="Grade.Assumption"/>);
/// what is not ours is what they must add up to. <c>ShopModelTests</c> walks each
/// persona thousands of times and holds the emergent rates to the published
/// ones below — add to cart near 6 %, abandonment near Baymard's 70.22 %,
/// conversion inside the 1.6–2.9 % range the sources give — so a probability
/// changed by hand that breaks a published rate is a red test, not a quieter
/// dashboard.
/// </summary>
public static class ShopModel
{
    private static readonly Source Tpcw = new("TPC-W specification: think time, exponential with mean 7 s", Grade.Primary,
        "https://www.tpc.org/tpcw/");
    private static readonly Source Baymard = new("Baymard Institute: average of 50 cart abandonment studies", Grade.Primary,
        "https://baymard.com/lists/cart-abandonment-rate");
    private static readonly Source Ours = Source.Assumption("chosen so the emergent rates match the published ones");

    // ---------------------------------------------------------------- the targets

    /// <summary>Carts that never become an order.</summary>
    public static readonly Sourced<double> CartAbandonment = new(0.7022, Baymard);

    /// <summary>Sessions that end in an order.</summary>
    public static readonly Sourced<(double Low, double High)> Conversion = new((0.016, 0.029),
        new("Conversion 1.6–2.9 % across sources, ~2.66 % (Dynamic Yield)", Grade.Secondary));

    /// <summary>Sessions that add something to the cart.</summary>
    public static readonly Sourced<double> AddToCartRate = new(0.06,
        new("Add-to-cart rate ~6 % (industry benchmarks)", Grade.Secondary));

    /// <summary>
    /// Why people leave a checkout, as weights: Baymard lets respondents give
    /// several reasons, so they sum past 100 and are used as relative weights.
    /// Recorded on the session's span when it abandons.
    /// </summary>
    public static readonly Sourced<IReadOnlyList<(string Reason, double Weight)>> AbandonmentReasons = new(
    [
        ("extra_costs", 0.40), ("slow_delivery", 0.20), ("card_trust", 0.19),
        ("forced_account", 0.18), ("long_checkout", 0.17), ("site_errors", 0.17)
    ], Baymard);

    // ---------------------------------------------------------------- time

    /// <summary>Seconds a person spends on a page before the next one.</summary>
    public static readonly Sourced<double> ThinkTimeMeanSeconds = new(7, Tpcw);

    /// <summary>TPC-W truncates the exponential at ten times its mean.</summary>
    public static readonly Sourced<double> ThinkTimeCapSeconds = new(70, Tpcw);

    /// <summary>Seconds between an agent's calls: agents do not read.</summary>
    public static readonly Sourced<double> AgentPauseMeanSeconds = new(1.5,
        Source.Assumption("an agent's calls are paced by its model, not by reading"));

    // ---------------------------------------------------------------- the checkout

    /// <summary>Authorisations the card issuer declines.</summary>
    public static readonly Sourced<double> CardDeclined = new(0.08,
        new("Card-not-present decline rates, commonly quoted at 5–15 %", Grade.Low));

    /// <summary>After a decline, the share who try once more with another card.</summary>
    public static readonly Sourced<double> RetryAfterDecline = new(0.5,
        Source.Assumption("half try another card"));

    /// <summary>People who answer yes to the analytics question.</summary>
    public static readonly Sourced<double> ConsentGranted = new(0.55,
        new("Consent acceptance varies widely by banner design; the middle of reported ranges", Grade.Low));

    /// <summary>People who read the shop in English rather than Spanish.</summary>
    public static readonly Sourced<double> EnglishShare = new(0.3,
        Source.Assumption("a Spanish shop with some foreign visitors"));

    // ---------------------------------------------------------------- the personas

    private static IReadOnlyList<Transition> T(params (Step To, double P)[] edges) =>
        [.. edges.Select(edge => new Transition(edge.To, edge.P))];

    /// <summary>Arrives on the home page and mostly browses.</summary>
    public static readonly Persona WindowShopper = new(
        "window-shopper", PersonaKind.Human,
        new(0.50, Source.Assumption("most visitors browse; 30–50 % of visitors search (vendor aggregates)")),
        Step.Home,
        new Dictionary<Step, IReadOnlyList<Transition>>
        {
            [Step.Home] = T((Step.Browse, 0.45), (Step.Search, 0.15), (Step.Product, 0.15), (Step.Exit, 0.25)),
            [Step.Browse] = T((Step.Product, 0.50), (Step.Browse, 0.15), (Step.Search, 0.05), (Step.Exit, 0.30)),
            [Step.Search] = T((Step.Product, 0.50), (Step.Search, 0.15), (Step.Browse, 0.05), (Step.Exit, 0.30)),
            [Step.Product] = T((Step.AddToCart, 0.05), (Step.Product, 0.15), (Step.Browse, 0.25), (Step.Search, 0.06), (Step.Exit, 0.49)),
            [Step.AddToCart] = T((Step.Cart, 0.55), (Step.Product, 0.10), (Step.Browse, 0.10), (Step.Exit, 0.25)),
            [Step.Cart] = T((Step.Checkout, 0.66), (Step.Browse, 0.09), (Step.Exit, 0.25)),
            [Step.Checkout] = T((Step.Pay, 0.72), (Step.Exit, 0.28)),
            [Step.Pay] = T((Step.Exit, 1.0)),
        },
        Ours);

    /// <summary>
    /// Arrives with something in mind and searches for it. Searchers convert two
    /// to three times as often (vendor aggregates, low grade), which is what its
    /// higher add-to-cart edge is for.
    /// </summary>
    public static readonly Persona Searcher = new(
        "searcher", PersonaKind.Human,
        new(0.36, Source.Assumption("30–50 % of visitors search (vendor aggregates, low grade)")),
        Step.Home,
        new Dictionary<Step, IReadOnlyList<Transition>>
        {
            [Step.Home] = T((Step.Search, 0.80), (Step.Browse, 0.10), (Step.Exit, 0.10)),
            [Step.Search] = T((Step.Product, 0.60), (Step.Search, 0.20), (Step.Exit, 0.20)),
            [Step.Browse] = T((Step.Product, 0.50), (Step.Search, 0.20), (Step.Exit, 0.30)),
            [Step.Product] = T((Step.AddToCart, 0.10), (Step.Product, 0.10), (Step.Search, 0.30), (Step.Exit, 0.50)),
            [Step.AddToCart] = T((Step.Cart, 0.60), (Step.Search, 0.15), (Step.Exit, 0.25)),
            [Step.Cart] = T((Step.Checkout, 0.68), (Step.Search, 0.07), (Step.Exit, 0.25)),
            [Step.Checkout] = T((Step.Pay, 0.72), (Step.Exit, 0.28)),
            [Step.Pay] = T((Step.Exit, 1.0)),
        },
        Ours);

    /// <summary>An external agent reading the catalogue over MCP (read-only today).</summary>
    public static readonly Persona McpAgent = new(
        "mcp-agent", PersonaKind.Agent,
        new(0.06, Source.Assumption("a small agent share, the number phase 11 exists to move")),
        Step.McpSearch,
        new Dictionary<Step, IReadOnlyList<Transition>>
        {
            [Step.McpSearch] = T((Step.McpProduct, 0.60), (Step.McpSearch, 0.20), (Step.Exit, 0.20)),
            [Step.McpProduct] = T((Step.McpProduct, 0.20), (Step.McpSearch, 0.30), (Step.Exit, 0.50)),
        },
        Source.Assumption("an agent comparing a few products before answering"));

    /// <summary>An external agent over UCP's REST binding.</summary>
    public static readonly Persona UcpAgent = new(
        "ucp-agent", PersonaKind.Agent,
        new(0.04, Source.Assumption("UCP is younger than MCP; fewer agents speak it")),
        Step.UcpSearch,
        new Dictionary<Step, IReadOnlyList<Transition>>
        {
            [Step.UcpSearch] = T((Step.UcpLookup, 0.60), (Step.UcpSearch, 0.20), (Step.Exit, 0.20)),
            [Step.UcpLookup] = T((Step.UcpSearch, 0.30), (Step.Exit, 0.70)),
        },
        Source.Assumption("an agent comparing a few products before answering"));

    /// <summary>
    /// A customer coming back to an order the simulation placed: half of them
    /// send something back. They can only return what the shopkeeper persona has
    /// delivered, so returns lag orders by as long as the shop takes to ship.
    /// </summary>
    public static readonly Persona Returner = new(
        "returner", PersonaKind.Human,
        new(0.02, Source.Assumption("a small share of visits are customers checking on an order")),
        Step.OpenOrder,
        new Dictionary<Step, IReadOnlyList<Transition>>
        {
            [Step.OpenOrder] = T((Step.RequestReturn, 0.50), (Step.Exit, 0.50)),
            [Step.RequestReturn] = T((Step.Exit, 1.0)),
        },
        Source.Assumption("online apparel return rates of 20–30 % are commonly quoted; half of those who come back"));

    /// <summary>
    /// The shopkeeper, in the backoffice: signs in, moves what is waiting — ships
    /// what is confirmed, marks delivered what was shipped — and looks at the
    /// review queue. Without it no order is ever delivered, so nothing could be
    /// returned and no reservation would ever be committed.
    /// </summary>
    public static readonly Persona Shopkeeper = new(
        "shopkeeper", PersonaKind.Staff,
        new(0.02, Source.Assumption("the shop's own visits to its backoffice, a few an hour")),
        Step.SignIn,
        new Dictionary<Step, IReadOnlyList<Transition>>
        {
            [Step.SignIn] = T((Step.MoveOrders, 0.75), (Step.ReviewQueue, 0.25)),
            [Step.MoveOrders] = T((Step.ReviewQueue, 0.20), (Step.Exit, 0.80)),
            [Step.ReviewQueue] = T((Step.MoveOrders, 0.30), (Step.Exit, 0.70)),
        },
        Source.Assumption("orders first, the queue when there is time"));

    /// <summary>
    /// Why things come back, as weights over the domain's five reasons. Size and
    /// fit lead by a distance in every survey of online apparel returns.
    /// </summary>
    public static readonly Sourced<IReadOnlyList<(string Reason, double Weight)>> ReturnReasons = new(
    [
        ("WrongSize", 0.50), ("NotAsDescribed", 0.20), ("ChangedMind", 0.12), ("Damaged", 0.10), ("WrongItem", 0.08)
    ], new("Retailer and logistics surveys of return reasons (size and fit first, around half)", Grade.Low));

    public static readonly IReadOnlyList<Persona> Personas = [WindowShopper, Searcher, McpAgent, UcpAgent, Returner, Shopkeeper];

    // ---------------------------------------------------------------- arrivals

    /// <summary>
    /// How busy each hour of the day is, as a share of the busiest one: quiet
    /// before dawn, a lunch bump, the peak in the evening.
    /// </summary>
    public static readonly Sourced<IReadOnlyList<double>> HourlyProfile = new(
    [
        0.20, 0.15, 0.10, 0.08, 0.08, 0.10, 0.18, 0.30, 0.42, 0.50, 0.55, 0.60,
        0.66, 0.62, 0.55, 0.52, 0.55, 0.62, 0.72, 0.85, 0.95, 1.00, 0.80, 0.45
    ], Source.Assumption("the usual shape of retail web traffic in Spain: an evening peak"));

    /// <summary>
    /// Sessions a minute at the busiest hour, in real time. For plausibility, not
    /// load: the laboratory is one machine beside the whole Aspire stack.
    /// </summary>
    public static readonly Sourced<double> PeakSessionsPerMinute = new(4,
        Source.Assumption("three to five at peak and about one at night keep one laptop responsive"));
}
