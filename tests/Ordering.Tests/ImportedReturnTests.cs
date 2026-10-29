using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// A refund that already happened on another platform (phase 16, S2b, decision
/// S2b-2; ADR 0066): a return born refunded, against the imported order, with
/// the amount and the source's reference, and no event — the money went back
/// last year, and nothing must refund or restock it again.
/// </summary>
public sealed class ImportedReturnTests
{
    private static readonly DateTimeOffset RefundedThen = new(2026, 4, 20, 16, 0, 0, TimeSpan.Zero);

    private static readonly Address Ana = Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");

    private static Order Delivered() =>
        Order.Imported(
            new OrderOrigin("shopify", "5002", "#1002"), OrderStatus.Delivered, CustomerId.New(),
            [
                new OrderLine(ProductId.New(), VariantId.New(), "SHIRT-M", "Camiseta", "M", null, new Money(20m, "EUR"), 2),
            ],
            Ana, Ana, new OrderShipping("shopify:standard", "Envío", Money.Zero("EUR"), null),
            new OrderTotals(new Money(40m, "EUR"), Money.Zero("EUR"), Money.Zero("EUR"), new Money(6.94m, "EUR"), new Money(40m, "EUR")),
            new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
            shippedAt: new DateTimeOffset(2026, 4, 3, 9, 0, 0, TimeSpan.Zero));

    private static ReturnLine Shirts(int quantity) => new(VariantId.New(), "SHIRT-M", quantity, ReturnReason.ChangedMind, null);

    [Fact]
    public void A_refund_that_happened_is_born_refunded_with_its_amount_and_reference_and_no_event()
    {
        var order = Delivered();

        var refund = ReturnRequest.Imported(order, [Shirts(1)], [], new Money(20m, "EUR"), "shopify:refund:7001", RefundedThen);

        Assert.Equal(ReturnStatus.Refunded, refund.Status);
        Assert.Equal((new Money(20m, "EUR"), "shopify:refund:7001"), (refund.RefundAmount, refund.RefundReference));
        Assert.Equal((order.Id, 1, RefundedThen), (refund.OrderId, refund.Number, refund.CreatedAt));
        Assert.Empty(refund.DomainEvents);
    }

    /// <summary>It holds its lines like any refunded return: what came back once cannot come back again.</summary>
    [Fact]
    public void What_was_refunded_is_not_returnable_again()
    {
        var order = Delivered();
        var refund = ReturnRequest.Imported(order, [Shirts(2)], [], new Money(40m, "EUR"), "shopify:refund:7001", RefundedThen);

        Assert.Equal(0, ReturnRequest.Returnable(order, [refund])["SHIRT-M"]);
        Assert.Throws<InvalidOperationException>(() =>
            ReturnRequest.Imported(order, [Shirts(1)], [refund], new Money(20m, "EUR"), "shopify:refund:7002", RefundedThen));
    }

    /// <summary>The checks opening makes, kept: lines the order had, a positive refund.</summary>
    [Fact]
    public void What_opening_refuses_importing_refuses_too()
    {
        var order = Delivered();

        Assert.Throws<InvalidOperationException>(() =>
            ReturnRequest.Imported(order, [Shirts(3)], [], new Money(60m, "EUR"), "shopify:refund:7001", RefundedThen));
        Assert.Throws<InvalidOperationException>(() =>
            ReturnRequest.Imported(order, [Shirts(1)], [], Money.Zero("EUR"), "shopify:refund:7001", RefundedThen));
    }

    /// <summary>The reason an imported refund carries, because the store recorded none — and opening a return here refuses it.</summary>
    [Fact]
    public void Only_history_returns_without_a_reason()
    {
        var order = Delivered();
        var unstated = new ReturnLine(VariantId.New(), "SHIRT-M", 1, ReturnReason.Unstated, null);

        Assert.Equal(ReturnStatus.Refunded, ReturnRequest.Imported(order, [unstated], [], new Money(20m, "EUR"), "shopify:refund:7001", RefundedThen).Status);
        Assert.Throws<InvalidOperationException>(() => ReturnRequest.Open(new ElGuerre.Tendero.Tests.TestClock(), order, [unstated], []));
    }

    /// <summary>
    /// One line without a reason is enough to refuse opening, however many say why.
    /// Found by phase 16 S2b's Stryker close: "any" made "all" survived.
    /// </summary>
    [Fact]
    public void A_return_opened_here_refuses_any_line_without_a_reason()
    {
        var order = Delivered();
        var unstated = new ReturnLine(VariantId.New(), "SHIRT-M", 1, ReturnReason.Unstated, null);

        Assert.Throws<InvalidOperationException>(() =>
            ReturnRequest.Open(new ElGuerre.Tendero.Tests.TestClock(), order, [Shirts(1), unstated], []));
    }

    /// <summary>An imported refund names the source's refund it was, or it cannot be told from another. Found by the same close.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void An_imported_refund_without_its_reference_is_refused(string reference) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            ReturnRequest.Imported(Delivered(), [Shirts(1)], [], new Money(20m, "EUR"), reference, RefundedThen));
}
