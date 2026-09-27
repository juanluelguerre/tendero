using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// A UCP checkout session (P11-1): what it lacks is derived from what it holds,
/// its life is Open → Completed | Canceled and nothing else, and it belongs to
/// the agent that opened it.
/// </summary>
public sealed class CheckoutSessionTests
{
    private static readonly TestClock Clock = new();
    private static readonly AgentId Claude = new("claude-desktop-test");
    private static readonly Address Madrid = Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");

    private static CheckoutSession Open() => CheckoutSession.Open(Clock, Claude, CartId.New(), OrderMode.Test, "es");

    [Fact]
    public void A_new_session_lacks_everything_but_its_lines_and_expires_in_six_hours()
    {
        var session = Open();

        Assert.Equal([CheckoutGap.BuyerEmail, CheckoutGap.Destination], session.Gaps(hasLines: true));
        Assert.Equal([CheckoutGap.Lines, CheckoutGap.BuyerEmail, CheckoutGap.Destination], session.Gaps(hasLines: false));
        Assert.Equal(Clock.GetUtcNow() + TimeSpan.FromHours(6), session.ExpiresAt);
        Assert.Equal((OrderMode.Test, CheckoutSessionStatus.Open), (session.Mode, session.Status));
    }

    [Fact]
    public void Described_whole_it_lacks_nothing_and_codes_are_kept_once_each()
    {
        var session = Open();

        session.Describe(Clock, new CheckoutBuyer("Ana", "Ruiz", "ana@example.com", null), Madrid, " standard ", ["BIENVENIDA10", "bienvenida10", " "], "en");

        Assert.Empty(session.Gaps(hasLines: true));
        Assert.Equal(("standard", "en"), (session.ShippingOptionCode, session.Culture));
        Assert.Equal(["BIENVENIDA10"], session.Coupons);

        // An update is a full replacement (UCP): the codes are the new ones, not both.
        session.Describe(Clock, new CheckoutBuyer("Ana", "Ruiz", "ana@example.com", null), Madrid, "standard", ["VERANO5"], "en");
        Assert.Equal(["VERANO5"], session.Coupons);
    }

    /// <summary>An option means nothing without somewhere to send it: dropping the destination drops the option.</summary>
    [Fact]
    public void Without_a_destination_the_option_goes_and_an_option_is_asked_for_only_once_there_is_one()
    {
        var session = Open();
        var buyer = new CheckoutBuyer(null, null, "ana@example.com", null);

        session.Describe(Clock, buyer, null, "standard", [], "es");
        Assert.Null(session.ShippingOptionCode);
        Assert.Equal([CheckoutGap.Destination], session.Gaps(hasLines: true));

        session.Describe(Clock, buyer, Madrid, null, [], "es");
        Assert.Equal([CheckoutGap.ShippingOption], session.Gaps(hasLines: true));
    }

    [Fact]
    public void It_completes_or_is_canceled_once_and_then_nothing_changes()
    {
        var completed = Open();
        var orderId = OrderId.New();
        completed.Complete(Clock, orderId);

        Assert.Equal((CheckoutSessionStatus.Completed, orderId), (completed.Status, completed.OrderId));
        Assert.Contains(completed.DomainEvents, e => e is CheckoutSessionCompleted { OrderId: var id } && id == orderId);
        Assert.Throws<InvalidOperationException>(() => completed.Cancel(Clock));
        Assert.Throws<InvalidOperationException>(() => completed.Describe(Clock, CheckoutBuyer.Nobody, null, null, [], "es"));

        var canceled = Open();
        canceled.Cancel(Clock);
        Assert.Contains(canceled.DomainEvents, e => e is CheckoutSessionCanceled);
        Assert.Throws<InvalidOperationException>(() => canceled.Complete(Clock, OrderId.New()));
        Assert.False(canceled.HasExpiredAt(Clock.GetUtcNow() + TimeSpan.FromDays(1)));
    }

    [Fact]
    public void It_belongs_to_the_agent_that_opened_it_and_expires_by_neglect()
    {
        var session = Open();
        var owner = new CommercePrincipal(null, Claude, "claude-desktop-test", null, ["agent"]);
        var other = new CommercePrincipal(null, new AgentId("other"), "other", null, ["agent"]);

        Assert.True(session.BelongsTo(owner));
        Assert.False(session.BelongsTo(other));
        Assert.False(session.BelongsTo(CommercePrincipal.Anonymous));

        Clock.Advance(TimeSpan.FromHours(5));
        session.Describe(Clock, CheckoutBuyer.Nobody, null, null, [], "es");
        Assert.False(session.HasExpiredAt(Clock.GetUtcNow() + TimeSpan.FromHours(5)));
        Assert.True(session.HasExpiredAt(Clock.GetUtcNow() + TimeSpan.FromHours(6)));
    }

    [Fact]
    public void A_session_belongs_to_somebody()
    {
        Assert.Throws<ArgumentException>(() => CheckoutSession.Open(Clock, new AgentId(" "), CartId.New(), OrderMode.Test, "es"));
    }

    [Fact]
    public void The_quote_shown_is_remembered_and_a_blank_one_is_refused()
    {
        var session = Open();

        session.Showed("abc");

        Assert.Equal("abc", session.ShownQuoteHash);
        Assert.Throws<ArgumentException>(() => session.Showed(" "));
    }

    [Fact]
    public void A_cart_takes_exactly_the_lines_it_is_given_merged_by_sku()
    {
        var cart = Cart.Start(Clock, "es", "EUR");
        CartLine Line(string sku, int quantity) => new(ProductId.New(), VariantId.New(), sku, LocalizedText.From("es", sku), null, null, quantity);
        cart.Add(Clock, Line("OLD", 1));

        cart.ReplaceLines(Clock, [Line("A", 2), Line("a", 1), Line("B", 3)]);

        Assert.Equal([("A", 3), ("B", 3)], cart.Lines.Select(line => (line.Sku, line.Quantity)));
        Assert.Throws<InvalidOperationException>(() => cart.ReplaceLines(Clock, [Line("A", Cart.MaximumQuantity + 1)]));
    }
}
