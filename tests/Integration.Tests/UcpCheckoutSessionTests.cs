using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Adapters;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.CheckoutSessions;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// UCP's checkout (P11-1) end to end on a real Postgres, through the slice the
/// REST and MCP bindings both dispatch to: a test credential's agent opens a
/// session, describes it, completes it and gets a rehearsal order; a live agent's
/// session stops at requires_escalation; another agent cannot see either.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UcpCheckoutSessionTests(PostgresFixture postgres)
{
    private const string Sku = "B05DEFG606-DEFAULT";

    private static readonly CheckoutDetails Described = new(
        new CheckoutBuyerDetails("Ana", "Ruiz", "ana@example.com", null), Shop.ShipTo, "standard", []);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_test_agent_opens_describes_and_completes_a_checkout_into_a_test_order()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var shop = await Shop.OpenAsync(postgres, nameof(A_test_agent_opens_describes_and_completes_a_checkout_into_a_test_order), new Agent("claude-desktop-test", test: true));
        await shop.StockAsync(Sku, 5, ct);

        var opened = await shop.SendAsync(new OpenCheckoutSessionCommand([new(Sku, 2), new("NOPE-1", 1)], CheckoutDetails.None, "es"), ct);
        Assert.Equal(CheckoutSessionOutcome.Ok, opened.Outcome);
        var session = opened.Session!;
        Assert.Equal(CheckoutState.Incomplete, session.State);
        Assert.Contains(session.Issues, issue => issue is { Code: CheckoutIssueCodes.ItemUnavailable, Target: "line:NOPE-1" });
        Assert.Contains(session.Issues, issue => issue is { Code: CheckoutIssueCodes.Missing, Target: "buyer.email" });

        var id = new CheckoutSessionId(Guid.Parse(session.Id));
        var ready = (await shop.SendAsync(new UpdateCheckoutSessionCommand(id, [new(Sku, 2)], Described, "es"), ct)).Session!;
        Assert.Equal((CheckoutState.ReadyForComplete, true, "standard"), (ready.State, ready.CanSettle, ready.SelectedShippingOption));
        Assert.NotNull(ready.Totals);

        var completed = await shop.SendAsync(new CompleteCheckoutSessionCommand(id, FakePaymentProvider.CardOk, "ucp-complete-1", "es"), ct);
        Assert.Equal((CheckoutSessionOutcome.Ok, CheckoutState.Completed), (completed.Outcome, completed.Session!.State));

        // A retry of a completion that went through buys nothing twice.
        var again = await shop.SendAsync(new CompleteCheckoutSessionCommand(id, FakePaymentProvider.CardOk, "ucp-complete-1", "es"), ct);
        Assert.Equal(completed.Session.OrderId, again.Session!.OrderId);

        await using var context = shop.Factory.Create();
        var order = await context.Orders.SingleAsync(ct);
        Assert.Equal((order.Id.ToString(), OrderMode.Test, "ana@example.com"), (completed.Session.OrderId, order.Mode, order.ContactEmail));
        Assert.Equal(ready.Totals!.Total, order.Total.Amount);
        Assert.Equal(CheckoutSessionStatus.Completed, (await context.CheckoutSessions.SingleAsync(ct)).Status);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_live_agents_session_stops_at_escalation_and_cannot_be_completed()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var shop = await Shop.OpenAsync(postgres, nameof(A_live_agents_session_stops_at_escalation_and_cannot_be_completed), new Agent("claude-desktop", test: false));

        var session = (await shop.SendAsync(new OpenCheckoutSessionCommand([new(Sku, 1)], Described, "es"), ct)).Session!;

        Assert.Equal((CheckoutState.RequiresEscalation, false), (session.State, session.CanSettle));
        Assert.Contains(session.Issues, issue => issue.Code == CheckoutIssueCodes.PaymentNotSettled);

        var refused = await shop.SendAsync(new CompleteCheckoutSessionCommand(new CheckoutSessionId(Guid.Parse(session.Id)), FakePaymentProvider.CardOk, "ucp-live-1", "es"), ct);
        Assert.Equal(CheckoutSessionOutcome.NotReady, refused.Outcome);

        await using var context = shop.Factory.Create();
        Assert.Empty(await context.Orders.ToListAsync(ct));
    }

    /// <summary>
    /// UCP's escalation, all the way round: the agent cannot pay live, so a person
    /// follows the continue link, the storefront takes over the session's cart, the
    /// person pays — and the agent's checkout reads completed, with the order.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_person_who_pays_for_a_handed_off_cart_completes_the_agents_checkout()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var agent = await Shop.OpenAsync(postgres, nameof(A_person_who_pays_for_a_handed_off_cart_completes_the_agents_checkout), new Agent("claude-desktop", test: false));
        await agent.StockAsync(Sku, 5, ct);
        var id = new CheckoutSessionId(Guid.Parse((await agent.SendAsync(new OpenCheckoutSessionCommand([new(Sku, 1)], Described, "en"), ct)).Session!.Id));

        await using var person = await Shop.OpenOnAsync(agent.Factory, new SystemPrincipalAccessor());
        var handOff = await person.AskAsync(new HandOffCheckoutSessionQuery(id), ct);
        Assert.Equal("en", handOff!.Culture);

        var chosen = (await person.ShippingOptionsAsync(handOff.CartToken, ct)).Options[0];
        var quote = await person.QuoteAsync(handOff.CartToken, chosen.Amount, ct, culture: "en");
        var placed = await person.PlaceOrderAsync(handOff.CartToken, chosen.Code, quote.InputHash, ct);
        Assert.Equal(PlaceOrderOutcome.Placed, placed.Outcome);

        var read = (await agent.AskAsync(new GetCheckoutSessionQuery(id, "en"), ct)).Session!;
        Assert.Equal((CheckoutState.Completed, placed.Order!.Id.ToString()), (read.State, read.OrderId));

        // Both found on the stack, 2026-09-27: a completed checkout shows what its
        // order froze — shipping included — not a fresh quote of a closed cart; and
        // the order is confirmed to the email the agent gave.
        Assert.Equal(placed.Order.Total.Amount, read.Totals!.Total);
        Assert.Equal(placed.Order.Shipping.Amount.Amount, read.Totals.Shipping);
        Assert.Equal("ana@example.com", placed.Order.ContactEmail);
        // Handed off once: a completed session hands nothing over again.
        Assert.Null(await person.AskAsync(new HandOffCheckoutSessionQuery(id), ct));
    }

    /// <summary>
    /// An agent asking for more than there is, is told at completion which line
    /// and how many there are — the same till, so the same answer as the
    /// storefront's — and nothing is charged or placed.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task More_than_there_is_is_an_issue_on_its_line_and_nothing_is_placed()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var shop = await Shop.OpenAsync(postgres, nameof(More_than_there_is_is_an_issue_on_its_line_and_nothing_is_placed), new Agent("claude-desktop-test", test: true));
        await shop.StockAsync(Sku, 1, ct);

        var id = new CheckoutSessionId(Guid.Parse((await shop.SendAsync(new OpenCheckoutSessionCommand([new(Sku, 2)], Described, "es"), ct)).Session!.Id));

        var refused = await shop.SendAsync(new CompleteCheckoutSessionCommand(id, FakePaymentProvider.CardOk, "ucp-short-1", "es"), ct);

        Assert.Equal(CheckoutSessionOutcome.NotPlaced, refused.Outcome);
        var issue = Assert.Single(refused.Session!.Issues);
        Assert.Equal((CheckoutIssueCodes.ItemUnavailable, $"line:{Sku}", "2 asked for, 1 available"), (issue.Code, issue.Target, issue.Detail));

        await using var context = shop.Factory.Create();
        Assert.Empty(await context.Orders.ToListAsync(ct));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_declined_card_places_nothing_and_a_canceled_session_changes_no_more()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var shop = await Shop.OpenAsync(postgres, nameof(A_declined_card_places_nothing_and_a_canceled_session_changes_no_more), new Agent("claude-desktop-test", test: true));
        // On the shelf, so that what refuses is the card: the till asks the
        // shelf before it asks the bank.
        await shop.StockAsync(Sku, 5, ct);

        var id = new CheckoutSessionId(Guid.Parse((await shop.SendAsync(new OpenCheckoutSessionCommand([new(Sku, 1)], Described, "es"), ct)).Session!.Id));

        var declined = await shop.SendAsync(new CompleteCheckoutSessionCommand(id, FakePaymentProvider.CardDeclined, "ucp-declined-1", "es"), ct);
        Assert.Equal(CheckoutSessionOutcome.NotPlaced, declined.Outcome);
        Assert.Contains(declined.Session!.Issues, issue => issue.Code == CheckoutIssueCodes.PaymentFailed);

        var canceled = await shop.SendAsync(new CancelCheckoutSessionCommand(id, "es"), ct);
        Assert.Equal(CheckoutState.Canceled, canceled.Session!.State);
        Assert.Equal(CheckoutSessionOutcome.NotOpen, (await shop.SendAsync(new UpdateCheckoutSessionCommand(id, [new(Sku, 2)], Described, "es"), ct)).Outcome);

        await using var context = shop.Factory.Create();
        Assert.Empty(await context.Orders.ToListAsync(ct));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Another_agent_cannot_see_a_session_and_a_person_cannot_open_one()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var name = nameof(Another_agent_cannot_see_a_session_and_a_person_cannot_open_one);

        await using var owner = await Shop.OpenAsync(postgres, name, new Agent("claude-desktop-test", test: true));
        var id = new CheckoutSessionId(Guid.Parse((await owner.SendAsync(new OpenCheckoutSessionCommand([new(Sku, 1)], CheckoutDetails.None, "es"), ct)).Session!.Id));

        await using var stranger = await Shop.OpenOnAsync(owner.Factory, new Agent("someone-else", test: true));
        Assert.Equal(CheckoutSessionOutcome.NotFound, (await stranger.AskAsync(new GetCheckoutSessionQuery(id, "es"), ct)).Outcome);

        await using var person = await Shop.OpenOnAsync(owner.Factory, new SystemPrincipalAccessor());
        Assert.Equal(CheckoutSessionOutcome.NotAnAgent, (await person.SendAsync(new OpenCheckoutSessionCommand([new(Sku, 1)], CheckoutDetails.None, "es"), ct)).Outcome);
    }

    /// <summary>
    /// Delegation (P11-3): a token exchanged for one acting for a person carries the
    /// person as the customer and the agent as the actor. The order is the person's —
    /// it is in their account — and the session still belongs to the agent.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task An_agent_acting_for_a_person_places_the_order_in_their_account()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var ana = CustomerId.New();
        await using var shop = await Shop.OpenAsync(postgres, nameof(An_agent_acting_for_a_person_places_the_order_in_their_account), new Agent("claude-desktop-test", test: true, customer: ana));
        await shop.StockAsync(Sku, 5, ct);

        var id = new CheckoutSessionId(Guid.Parse((await shop.SendAsync(new OpenCheckoutSessionCommand([new(Sku, 1)], Described, "es"), ct)).Session!.Id));
        var completed = await shop.SendAsync(new CompleteCheckoutSessionCommand(id, FakePaymentProvider.CardOk, "ucp-for-ana", "es"), ct);

        await using var context = shop.Factory.Create();
        var order = await context.Orders.SingleAsync(ct);
        Assert.Equal((completed.Session!.OrderId, ana), (order.Id.ToString(), order.CustomerId));
        Assert.Equal(new AgentId("claude-desktop-test"), (await context.CheckoutSessions.SingleAsync(ct)).Owner);
    }

    /// <summary>
    /// P11-5: what the verified mandate approved travels with the completion, is
    /// checked again in the transaction that places the order, and is frozen onto
    /// it. A total the person did not approve places nothing and charges nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_mandate_is_frozen_onto_the_order_and_a_total_it_did_not_approve_places_nothing()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var shop = await Shop.OpenAsync(postgres, nameof(A_mandate_is_frozen_onto_the_order_and_a_total_it_did_not_approve_places_nothing), new Agent("claude-desktop-test", test: true));
        await shop.StockAsync(Sku, 5, ct);

        var ready = (await shop.SendAsync(new OpenCheckoutSessionCommand([new(Sku, 1)], Described, "es"), ct)).Session!;
        var id = new CheckoutSessionId(Guid.Parse(ready.Id));
        MandateScope Approving(decimal total) =>
            new("mandate.checkout.1", "dev-platform-claude-desktop-test", "hash-1", new Money(total, "EUR"), DateTimeOffset.UtcNow.AddMinutes(15));

        var cheaper = await shop.SendAsync(new CompleteCheckoutSessionCommand(id, FakePaymentProvider.CardOk, "ucp-mandate-1", "es", Approving(ready.Totals!.Total - 1m)), ct);
        Assert.Equal(CheckoutSessionOutcome.NotPlaced, cheaper.Outcome);
        Assert.Contains(cheaper.Session!.Issues, issue => issue.Code == CheckoutIssueCodes.MandateScopeMismatch);

        await using (var before = shop.Factory.Create())
            Assert.Empty(await before.Orders.ToListAsync(ct));

        var completed = await shop.SendAsync(new CompleteCheckoutSessionCommand(id, FakePaymentProvider.CardOk, "ucp-mandate-2", "es", Approving(ready.Totals.Total)), ct);
        Assert.Equal(CheckoutState.Completed, completed.Session!.State);

        await using var context = shop.Factory.Create();
        var mandate = (await context.Orders.SingleAsync(ct)).Mandate!;
        Assert.Equal(("mandate.checkout.1", "dev-platform-claude-desktop-test", "hash-1", ready.Totals.Total, "EUR"),
            (mandate.Kind, mandate.Issuer, mandate.Reference, mandate.ApprovedTotal, mandate.Currency));
    }

    /// <summary>
    /// P11-6: the shop's own rules for agents, on top of whatever the person allows —
    /// an order above what an untrusted agent may spend is refused, and so is the
    /// second order in an hour when the shop allows one. Each refusal names its rule,
    /// charges nothing, and a placed order remembers the agent that placed it.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_shops_rules_for_agents_refuse_over_the_cap_and_over_the_hourly_count()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var shop = await Shop.OpenAsync(postgres, nameof(The_shops_rules_for_agents_refuse_over_the_cap_and_over_the_hourly_count),
            new Agent("claude-desktop-test", test: true),
            new Dictionary<string, string?> { ["AgentPolicy:OrderCaps:Untrusted"] = "60", ["AgentPolicy:MaxOrdersPerHour"] = "1" });
        await shop.StockAsync(Sku, 10, ct);

        async Task<CheckoutSessionResult> BuyAsync(int quantity, string key)
        {
            var id = new CheckoutSessionId(Guid.Parse((await shop.SendAsync(new OpenCheckoutSessionCommand([new(Sku, quantity)], Described, "es"), ct)).Session!.Id));
            return await shop.SendAsync(new CompleteCheckoutSessionCommand(id, FakePaymentProvider.CardOk, key, "es"), ct);
        }

        var tooMuch = await BuyAsync(2, "ucp-rules-1");
        Assert.Equal(CheckoutSessionOutcome.NotPlaced, tooMuch.Outcome);
        // Found on the stack: refused by the shop's rules is not "incomplete" — a
        // person can finish it at the continue URL, which is UCP's escalation.
        Assert.Equal(CheckoutState.RequiresEscalation, tooMuch.Session!.State);
        Assert.DoesNotContain(tooMuch.Session.Issues, issue => issue.Code == CheckoutIssueCodes.PaymentNotSettled);
        Assert.StartsWith("order_over_trust_cap", tooMuch.Session!.Issues.Single(issue => issue.Code == CheckoutIssueCodes.AgentRefused).Detail, StringComparison.Ordinal);

        var first = await BuyAsync(1, "ucp-rules-2");
        Assert.Equal(CheckoutState.Completed, first.Session!.State);

        var second = await BuyAsync(1, "ucp-rules-3");
        Assert.StartsWith("orders_per_hour", second.Session!.Issues.Single(issue => issue.Code == CheckoutIssueCodes.AgentRefused).Detail, StringComparison.Ordinal);

        await using var context = shop.Factory.Create();
        Assert.Equal(new AgentId("claude-desktop-test"), (await context.Orders.SingleAsync(ct)).PlacedBy);
    }

    private sealed class Agent(string id, bool test, CustomerId? customer = null) : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } =
            new(customer, new AgentId(id), id, id, ["agent"]) { IsTest = test, Standing = AgentStanding.Unknown };
    }
}
