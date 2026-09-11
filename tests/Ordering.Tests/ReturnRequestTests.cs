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
                    ImageId: null, new Money(line.Price, "EUR"), line.Quantity))
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

        var request = ReturnRequest.Open(Clock, order, [Line("SHIRT", 2)], earlier: []);

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
            () => ReturnRequest.Open(Clock, order, [Line(order.Lines[0].Sku)], earlier: []));
    }

    [Fact]
    public void A_return_cannot_ask_for_more_than_was_bought()
    {
        var order = Delivered(("SHIRT", 20m, 2));

        var exception = Assert.Throws<InvalidOperationException>(
            () => ReturnRequest.Open(Clock, order, [Line("SHIRT", 3)], earlier: []));

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
            () => ReturnRequest.Open(Clock, order, [Line("SHIRT"), Line("SHIRT"), Line("SHIRT")], earlier: []));
    }

    // ---------- Across returns ----------

    /// <summary>
    /// The defect the UI review found on 2026-09-11: one shirt bought, two
    /// returns opened for it, both refunded and both restocked. Every check above
    /// held — each request, on its own, asked for no more than was bought — and
    /// that is exactly the gap: the quantity a shopper may still send back is a
    /// fact about the ORDER and every return already opened against it, not
    /// about one request.
    /// </summary>
    [Fact]
    public void A_second_return_cannot_claim_what_an_open_one_already_claims()
    {
        var order = Delivered(("SHIRT", 20m, 1));
        var first = ReturnRequest.Open(Clock, order, [Line("SHIRT")], earlier: []);

        var exception = Assert.Throws<InvalidOperationException>(
            () => ReturnRequest.Open(Clock, order, [Line("SHIRT")], earlier: [first]));

        Assert.Contains("1 asked to return, 0 left", exception.Message);
    }

    /// <summary>
    /// Two of three are coming back, so one is still the shopper's to return —
    /// and two more are not.
    /// </summary>
    [Fact]
    public void What_is_left_to_return_is_what_was_bought_minus_what_is_already_coming_back()
    {
        var order = Delivered(("SHIRT", 20m, 3), ("PANS", 44.95m, 1));
        var first = ReturnRequest.Open(Clock, order, [Line("SHIRT", 2)], earlier: []);

        Assert.Equal(1, ReturnRequest.Returnable(order, [first])["SHIRT"]);
        Assert.Equal(1, ReturnRequest.Returnable(order, [first])["PANS"]);

        ReturnRequest.Open(Clock, order, [Line("SHIRT")], earlier: [first]);
        Assert.Throws<InvalidOperationException>(
            () => ReturnRequest.Open(Clock, order, [Line("SHIRT", 2)], earlier: [first]));
    }

    /// <summary>
    /// A return the shop refused, or one the shopper withdrew, is not coming
    /// back — so its quantity is the shopper's to ask for again. Everything
    /// else, refunded included, still holds its units: a refunded shirt cannot
    /// be refunded twice.
    /// </summary>
    [Theory]
    [InlineData(ReturnStatus.Requested, 0)]
    [InlineData(ReturnStatus.Approved, 0)]
    [InlineData(ReturnStatus.Received, 0)]
    [InlineData(ReturnStatus.Refunded, 0)]
    [InlineData(ReturnStatus.Rejected, 1)]
    [InlineData(ReturnStatus.Cancelled, 1)]
    public void Only_a_return_that_is_not_coming_back_gives_its_quantity_back(
        ReturnStatus status, int left)
    {
        var order = Delivered(("SHIRT", 20m, 1));
        var first = ReturnRequest.Open(Clock, order, [Line("SHIRT")], earlier: []);

        MoveTo(first, status);

        Assert.Equal(left, ReturnRequest.Returnable(order, [first])["SHIRT"]);
    }

    /// <summary>
    /// Each return is numbered within its order, counting every one opened
    /// before it — refused and withdrawn ones included, because the number is
    /// what two requests racing for the same order collide on (a unique index
    /// on order and number), and a number handed out twice would let both in.
    /// It is also what tells two returns of one order apart on a screen, where
    /// the first segment of a GUID v7 is identical for everything opened in the
    /// same second.
    /// </summary>
    [Fact]
    public void Each_return_of_an_order_is_numbered_in_the_order_it_was_opened()
    {
        var order = Delivered(("SHIRT", 20m, 3));

        var first = ReturnRequest.Open(Clock, order, [Line("SHIRT")], earlier: []);
        first.Reject(Clock, "no");
        var second = ReturnRequest.Open(Clock, order, [Line("SHIRT")], earlier: [first]);

        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
    }

    /// <summary>
    /// A return from a DIFFERENT order passed in as "earlier" is a caller bug,
    /// and a quiet one: counted, it would refuse a legitimate return; ignored, it
    /// would hide that the caller is reading the wrong rows.
    /// </summary>
    [Fact]
    public void Returns_of_another_order_are_refused_as_the_earlier_ones()
    {
        var order = Delivered(("SHIRT", 20m, 1));
        var stranger = ReturnRequest.Open(Clock, Delivered(("SHIRT", 20m, 1)), [Line("SHIRT")], earlier: []);

        Assert.Throws<ArgumentException>(
            () => ReturnRequest.Open(Clock, order, [Line("SHIRT")], earlier: [stranger]));
    }

    [Fact]
    public void A_return_for_something_that_was_never_bought_is_refused()
    {
        var order = Delivered(("SHIRT", 20m, 1));

        Assert.Throws<InvalidOperationException>(
            () => ReturnRequest.Open(Clock, order, [Line("PANS")], earlier: []));
    }

    [Fact]
    public void A_return_needs_at_least_one_line_and_a_positive_quantity()
    {
        var order = Delivered(("SHIRT", 20m, 1));

        Assert.Throws<InvalidOperationException>(() => ReturnRequest.Open(Clock, order, [], earlier: []));
        Assert.Throws<InvalidOperationException>(
            () => ReturnRequest.Open(Clock, order, [Line("SHIRT", 0)], earlier: []));
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

        var request = ReturnRequest.Open(Clock, order, [Line("SHIRT", 2)], earlier: []);

        // Two of three at 20, with no order-level discount and no tax in the
        // builder's derived totals: 40 flat.
        Assert.Equal(new Money(40m, "EUR"), request.RefundableFrom(order));
    }

    /// <summary>
    /// A returned line gives back its share of the order-level discount too. A
    /// shop that refunded the list price on a discounted order would pay out
    /// more than it took.
    ///
    /// And no tax is added to it: prices include VAT (ADR 0028), so what the
    /// line cost already carried its tax. The refund used to add a share of the
    /// order's tax on top — which, on an order whose tax included the
    /// shipping's, also handed back VAT on a delivery that is not refunded.
    /// </summary>
    [Fact]
    public void A_returned_line_carries_its_share_of_the_discount_with_the_tax_already_inside()
    {
        var order = OrderBuilder.Default()
            .WithLines(
                new OrderLine(ProductId.New(), VariantId.New(), "SHIRT", "A shirt", null,
                    ImageId: null, new Money(50m, "EUR"), 2))
            .WithShipping(new OrderShipping("standard", "Standard", new Money(4.95m, "EUR"), 3))
            .WithTotals(new OrderTotals(
                Subtotal: new Money(100m, "EUR"),
                DiscountTotal: new Money(10m, "EUR"),
                Shipping: new Money(4.95m, "EUR"),
                TaxTotal: new Money(16.48m, "EUR"),
                Total: new Money(94.95m, "EUR")))
            .InStatus(OrderStatus.Delivered);

        var request = ReturnRequest.Open(Clock, order, [Line("SHIRT")], earlier: []);

        // One of two: 50 of a 100 subtotal is half. 50 − 5 of discount, and the
        // tax is inside the 45 already.
        Assert.Equal(new Money(45m, "EUR"), request.RefundableFrom(order));
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
            .Open(Clock, order, [Line("SHIRT")], earlier: [])
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
        return ReturnRequest.Open(Clock, order, [Line("SHIRT", 2)], earlier: []);
    }

    private static void MoveTo(ReturnRequest request, ReturnStatus status)
    {
        switch (status)
        {
            case ReturnStatus.Requested:
                break;
            case ReturnStatus.Approved:
                request.Approve(Clock);
                break;
            case ReturnStatus.Received:
                request.Approve(Clock);
                request.Receive(Clock);
                break;
            case ReturnStatus.Refunded:
                request.Approve(Clock);
                request.Receive(Clock);
                request.Refund(Clock, new Money(20m, "EUR"), "ref_1");
                break;
            case ReturnStatus.Rejected:
                request.Reject(Clock, "no");
                break;
            case ReturnStatus.Cancelled:
                request.Cancel(Clock);
                break;
        }
    }
}
