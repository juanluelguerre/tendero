using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// Returns, and the transition table walked in full — the same treatment
/// <c>Order.AllowedTransitions</c> and <c>Reservation</c> get, for the same
/// reason: the table is the single source of truth for the lifecycle, so the
/// test writes the matrix out by hand as a second, independent declaration.
///
/// The reason this is a separate aggregate at all is the first test below: a
/// return is **per line**, and an order-level state machine cannot say "two of
/// the three shirts came back" without a combinatorial explosion.
/// </summary>
public sealed class ReturnRequestTests
{
    private static readonly TestClock Clock = new();

    private static Order Delivered(params (string Sku, decimal Price, int Quantity)[] lines)
    {
        var order = OrderBuilder.Default()
            .WithLines(
            [
                .. lines.Select(line => new OrderLine(
                    ProductId.New(), VariantId.New(), line.Sku, "A product", null,
                    new Money(line.Price, "EUR"), line.Quantity))
            ])
            .Build();

        order.AuthorizePayment(Clock);
        order.Confirm(Clock);
        order.Ship(Clock);
        order.Deliver(Clock);

        return order;
    }

    private static ReturnLine Line(string sku, int quantity = 1,
        ReturnReason reason = ReturnReason.WrongSize) =>
        new(VariantId.New(), sku, quantity, reason, null);

    // ---------- Opening ----------

    /// <summary>
    /// The whole reason returns are their own aggregate: three shirts bought,
    /// two coming back, and the order still says three were sold.
    /// </summary>
    [Fact]
    public void A_return_is_for_some_of_the_lines_and_not_for_the_order()
    {
        var order = Delivered(("SHIRT", 20m, 3), ("PANS", 44.95m, 1));

        var request = ReturnRequest.Open(Clock, order, [Line("SHIRT", 2)]);

        Assert.Equal(ReturnStatus.Requested, request.Status);
        Assert.Equal(("SHIRT", 2), (request.Lines.Single().Sku, request.Lines.Single().Quantity));
        Assert.Equal(3, order.Lines.First(line => line.Sku == "SHIRT").Quantity);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.PaymentAuthorized)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Cancelled)]
    public void Only_a_delivered_order_can_be_returned(OrderStatus status)
    {
        var order = OrderBuilder.Default().InStatus(status);

        Assert.Throws<InvalidOperationException>(
            () => ReturnRequest.Open(Clock, order, [Line(order.Lines[0].Sku)]));
    }

    [Fact]
    public void A_return_cannot_ask_for_more_than_was_bought()
    {
        var order = Delivered(("SHIRT", 20m, 2));

        var exception = Assert.Throws<InvalidOperationException>(
            () => ReturnRequest.Open(Clock, order, [Line("SHIRT", 3)]));

        Assert.Contains("3 asked to return, 2 bought", exception.Message);
    }

    /// <summary>
    /// Two lines of the same SKU are one quantity, so splitting a return across
    /// them must not slip past the check. It is the same class of bug a property
    /// test found in the promotion engine: matching by key instead of counting.
    /// </summary>
    [Fact]
    public void Two_return_lines_of_one_sku_are_counted_together()
    {
        var order = Delivered(("SHIRT", 20m, 2));

        Assert.Throws<InvalidOperationException>(
            () => ReturnRequest.Open(Clock, order, [Line("SHIRT"), Line("SHIRT"), Line("SHIRT")]));
    }

    [Fact]
    public void A_return_for_something_that_was_never_bought_is_refused()
    {
        var order = Delivered(("SHIRT", 20m, 1));

        Assert.Throws<InvalidOperationException>(
            () => ReturnRequest.Open(Clock, order, [Line("PANS")]));
    }

    [Fact]
    public void A_return_needs_at_least_one_line_and_a_positive_quantity()
    {
        var order = Delivered(("SHIRT", 20m, 1));

        Assert.Throws<InvalidOperationException>(() => ReturnRequest.Open(Clock, order, []));
        Assert.Throws<InvalidOperationException>(
            () => ReturnRequest.Open(Clock, order, [Line("SHIRT", 0)]));
    }

    // ---------- The window ----------

    [Fact]
    public void The_window_opens_on_delivery_and_closes_fourteen_days_later()
    {
        var clock = new TestClock();
        var order = OrderBuilder.Default().InStatus(OrderStatus.Shipped);

        Assert.False(ReturnRequest.WindowIsOpen(order, clock.GetUtcNow()));

        order.Deliver(clock);
        var delivered = clock.GetUtcNow();

        Assert.True(ReturnRequest.WindowIsOpen(order, delivered));
        Assert.True(ReturnRequest.WindowIsOpen(order, delivered + ReturnRequest.Window));
        Assert.False(ReturnRequest.WindowIsOpen(
            order, delivered + ReturnRequest.Window + TimeSpan.FromSeconds(1)));
    }

    // ---------- The transition table ----------

    [Fact]
    public void A_request_can_be_approved_rejected_or_cancelled()
    {
        Fresh().Approve(Clock);
        Fresh().Reject(Clock, "outside the window");
        Fresh().Cancel(Clock);
    }

    [Fact]
    public void An_approved_return_is_received_or_cancelled_and_never_refunded_directly()
    {
        var approved = Fresh();
        approved.Approve(Clock);

        // Refunding before the goods are back is paying for a parcel that may
        // never arrive.
        Assert.Throws<InvalidOperationException>(
            () => approved.Refund(Clock, new Money(20m, "EUR"), "ref_1"));

        approved.Receive(Clock);
        Assert.Equal(ReturnStatus.Received, approved.Status);
    }

    /// <summary>
    /// Rejected FROM Received, which is the edge that is easy to leave out: the
    /// parcel arrived and the goods are not what the reason claimed. It is the
    /// whole point of receiving and refunding being two steps.
    /// </summary>
    [Fact]
    public void A_received_return_can_still_be_refused_after_it_is_looked_at()
    {
        var received = Fresh();
        received.Approve(Clock);
        received.Receive(Clock);

        received.Reject(Clock, "It came back worn.");

        Assert.Equal(ReturnStatus.Rejected, received.Status);
        Assert.Equal("It came back worn.", received.Resolution);
    }

    [Theory]
    [InlineData(ReturnStatus.Refunded)]
    [InlineData(ReturnStatus.Rejected)]
    [InlineData(ReturnStatus.Cancelled)]
    public void Nothing_leaves_a_resolved_return(ReturnStatus resolved)
    {
        var request = Fresh();

        switch (resolved)
        {
            case ReturnStatus.Refunded:
                request.Approve(Clock);
                request.Receive(Clock);
                request.Refund(Clock, new Money(20m, "EUR"), "ref_1");
                break;
            case ReturnStatus.Rejected:
                request.Reject(Clock, "no");
                break;
            default:
                request.Cancel(Clock);
                break;
        }

        Assert.Throws<InvalidOperationException>(() => request.Approve(Clock));
        Assert.Throws<InvalidOperationException>(() => request.Receive(Clock));
        Assert.Throws<InvalidOperationException>(() => request.Cancel(Clock));
        Assert.Throws<InvalidOperationException>(
            () => request.Refund(Clock, new Money(20m, "EUR"), "ref_2"));
    }

    /// <summary>
    /// The events the outbox carries. Receiving is the one that restocks, and it
    /// has to be distinguishable from approving — a shop that restocked on
    /// approval would be selling parcels still in the post.
    /// </summary>
    [Fact]
    public void Receiving_and_approving_are_two_different_facts()
    {
        var request = Fresh();

        request.Approve(Clock);
        Assert.Single(request.DomainEvents.OfType<ReturnApproved>());
        Assert.Empty(request.DomainEvents.OfType<ReturnReceived>());

        request.Receive(Clock);
        Assert.Single(request.DomainEvents.OfType<ReturnReceived>());
    }

    /// <summary>
    /// Cancelling raises nothing, deliberately: nobody downstream acts on a
    /// shopper changing their mind before the parcel moved.
    /// </summary>
    [Fact]
    public void Cancelling_tells_nobody()
    {
        var request = Fresh();
        var before = request.DomainEvents.Count;

        request.Cancel(Clock);

        Assert.Equal(before, request.DomainEvents.Count);
    }

    // ---------- What comes back ----------

    [Fact]
    public void The_refund_is_the_returned_lines_at_the_price_that_was_frozen()
    {
        var order = Delivered(("SHIRT", 20m, 3));

        var request = ReturnRequest.Open(Clock, order, [Line("SHIRT", 2)]);

        // Two of three at 20, with no order-level discount and no tax in the
        // builder's derived totals: 40 flat.
        Assert.Equal(new Money(40m, "EUR"), request.RefundableFrom(order));
    }

    /// <summary>
    /// A returned line gives back its share of the order-level discount too. A
    /// shop that refunded the list price on a discounted order would pay out
    /// more than it took.
    /// </summary>
    [Fact]
    public void A_returned_line_carries_its_share_of_the_discount_and_the_tax()
    {
        var order = OrderBuilder.Default()
            .WithLines(
                new OrderLine(ProductId.New(), VariantId.New(), "SHIRT", "A shirt", null,
                    new Money(50m, "EUR"), 2))
            .WithShipping(new OrderShipping("standard", "Standard", new Money(4.95m, "EUR"), 3))
            .WithTotals(new OrderTotals(
                Subtotal: new Money(100m, "EUR"),
                DiscountTotal: new Money(10m, "EUR"),
                Shipping: new Money(4.95m, "EUR"),
                TaxTotal: new Money(18.90m, "EUR"),
                Total: new Money(113.85m, "EUR")))
            .InStatus(OrderStatus.Delivered);

        var request = ReturnRequest.Open(Clock, order, [Line("SHIRT")]);

        // One of two: 50 of a 100 subtotal is half. 50 − 5 discount + 9.45 tax.
        Assert.Equal(new Money(54.45m, "EUR"), request.RefundableFrom(order));
    }

    /// <summary>
    /// Shipping is not refunded, and that is a declared simplification rather
    /// than an oversight — it is also the rule most likely to be wrong for a
    /// given jurisdiction, which is why it is pinned by a test instead of living
    /// in a comment.
    /// </summary>
    [Fact]
    public void Shipping_does_not_come_back()
    {
        var order = Delivered(("SHIRT", 20m, 1));

        Assert.Equal(new Money(20m, "EUR"), ReturnRequest
            .Open(Clock, order, [Line("SHIRT")])
            .RefundableFrom(order));

        Assert.Equal(new Money(4.95m, "EUR"), order.Totals.Shipping);
    }

    [Fact]
    public void A_refund_records_the_amount_and_the_providers_reference()
    {
        var request = Fresh();
        request.Approve(Clock);
        request.Receive(Clock);

        request.Refund(Clock, new Money(20m, "EUR"), "ref_abc");

        Assert.Equal(new Money(20m, "EUR"), request.RefundAmount);
        Assert.Equal("ref_abc", request.RefundReference);
        Assert.Equal(new Money(20m, "EUR"), Assert.Single(
            request.DomainEvents.OfType<ReturnRefunded>()).Amount);
    }

    [Fact]
    public void A_refund_of_nothing_is_refused()
    {
        var request = Fresh();
        request.Approve(Clock);
        request.Receive(Clock);

        Assert.Throws<InvalidOperationException>(
            () => request.Refund(Clock, Money.Zero("EUR"), "ref_1"));
    }

    private static ReturnRequest Fresh()
    {
        var order = Delivered(("SHIRT", 20m, 3));
        return ReturnRequest.Open(Clock, order, [Line("SHIRT", 2)]);
    }
}
