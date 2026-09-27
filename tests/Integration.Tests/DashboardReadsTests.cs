using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Adapters;
using ElGuerre.Tendero.Ordering.Features.CheckoutSessions;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The dashboard's reads on a real Postgres (P11-7): every query <c>IShopHistory</c>
/// and the audit's denial history send translates, and the figures come out of what
/// the shop actually did — a person's order, an agent's rehearsal, an agent refused.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DashboardReadsTests(PostgresFixture postgres)
{
    private const string Sku = "B05DEFG606-DEFAULT";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_dashboard_reads_what_the_shop_did()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var agent = await Shop.OpenAsync(postgres, nameof(The_dashboard_reads_what_the_shop_did), new Agent(),
            new Dictionary<string, string?> { ["AgentPolicy:OrderCaps:Untrusted"] = "1" });
        await agent.StockAsync(Sku, 10, ct);

        // A person buys: one sale, one cart checked out, one new customer.
        await using var person = await Shop.OpenOnAsync(agent.Factory, new SystemPrincipalAccessor());
        var cart = await person.AddToCartAsync(null, Sku, 1, ct);
        var option = (await person.ShippingOptionsAsync(cart.Token, ct)).Options[0];
        var quote = await person.QuoteAsync(cart.Token, option.Amount, ct);
        Assert.Equal(PlaceOrderOutcome.Placed, (await person.PlaceOrderAsync(cart.Token, option.Code, quote.InputHash, ct)).Outcome);

        // An agent is refused by the shop's cap: a denial in the audit, no order.
        var session = (await agent.SendAsync(new OpenCheckoutSessionCommand([new(Sku, 1)],
            new CheckoutDetails(new CheckoutBuyerDetails("Ana", "Ruiz", "ana@example.com", null), Shop.ShipTo, "standard", []), "es"), ct)).Session!;
        await agent.SendAsync(new CompleteCheckoutSessionCommand(new CheckoutSessionId(Guid.Parse(session.Id)), FakePaymentProvider.CardOk, "dash-1", "es"), ct);

        var sales = await agent.AskAsync(new GetSalesSummaryQuery(DashboardPeriod.Day), ct);
        Assert.Equal((1, 1), (sales.Current.Orders, sales.Current.Units));
        Assert.Equal(Sku, Assert.Single(sales.BestSellers).Sku);

        var funnel = await agent.AskAsync(new GetCheckoutFunnelQuery(DashboardPeriod.Day), ct);
        Assert.Equal((1, 1), (funnel.CheckedOut, funnel.NewCustomers));

        var operations = await agent.AskAsync(new GetOperationsSummaryQuery(DashboardPeriod.Day), ct);
        Assert.Equal(0m, operations.CancellationRate);

        var agents = await agent.AskAsync(new GetAgentSalesQuery(DashboardPeriod.Day), ct);
        Assert.Equal((1, 0), (agents.Orders, agents.AgentOrders));

        var denials = await agent.ReadAsync<IAgentDenialHistory, IReadOnlyList<AuditEntry>>(history => history.AgentDenialsSinceAsync(DateTimeOffset.UtcNow.AddHours(-1), ct));
        var denial = Assert.Single(denials);
        Assert.Equal(new AgentId("claude-desktop-test"), denial.Agent);
        Assert.StartsWith("agent_refused: order_over_trust_cap", denial.Reason, StringComparison.Ordinal);
    }

    private sealed class Agent : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } =
            new(null, new AgentId("claude-desktop-test"), "claude-desktop-test", "claude-desktop-test", ["agent"]) { IsTest = true, Standing = AgentStanding.Unknown };
    }
}
