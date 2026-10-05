using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// Returns opened in a period, whatever became of them since (S-3a, ruling 4):
/// units by reason and by SKU — aggregates and products only, no order, no
/// customer and no comment, because a hosted model may read them.
/// </summary>
public sealed class ReturnsSummaryTests
{
    private static OrderLine Line(string sku, int quantity) =>
        new(ProductId.New(), VariantId.New(), sku, sku, null, null, new Money(10m, "EUR"), quantity);

    private static (ReturnsSummary Summary, DashboardWindow Window) Summarise(TestClock now, int limit, params ReturnRequest[] returns)
    {
        var window = DashboardWindow.Ending(now.GetUtcNow(), DashboardPeriod.Week);
        return (ReturnsFigures.Of(DashboardPeriod.Week, limit, window, returns), window);
    }

    [Fact]
    public void Only_returns_opened_in_the_window_count_whatever_their_status_now()
    {
        var now = new TestClock();
        var before = new TestClock(now.GetUtcNow().AddDays(-20));
        var old = Delivered(before, Line("OLD", 3));
        var oldReturn = ReturnRequest.Open(before, old, [new(old.Lines[0].VariantId, "OLD", 3, ReturnReason.Damaged, null)], earlier: []);

        var order = Delivered(now, Line("SHIRT", 4));
        var refunded = ReturnRequest.Open(now, order, [new(order.Lines[0].VariantId, "SHIRT", 2, ReturnReason.WrongSize, "too small, call me at 600 000 000")], earlier: []);
        refunded.Approve(now);
        refunded.Receive(now);
        refunded.Refund(now, new Money(20m, "EUR"), "ref-1");

        var (summary, _) = Summarise(now, 10, oldReturn, refunded);

        Assert.Equal((1, 2), (summary.Returns, summary.Units));
        Assert.Equal(2, summary.ByReason.Single(row => row.Reason == ReturnReason.WrongSize).Units);
        Assert.Equal(0, summary.ByReason.Single(row => row.Reason == ReturnReason.Damaged).Units);
    }

    [Fact]
    public void Every_reason_is_listed_in_order_zero_included()
    {
        var (summary, _) = Summarise(new TestClock(), 10);

        Assert.Equal(Enum.GetValues<ReturnReason>(), summary.ByReason.Select(row => row.Reason));
        Assert.All(summary.ByReason, row => Assert.Equal(0, row.Units));
        Assert.Equal((0, 0, 0), (summary.Returns, summary.Units, summary.SkuTotal));
    }

    [Fact]
    public void Skus_rank_by_units_then_code_each_with_its_reasons()
    {
        var now = new TestClock();
        var order = Delivered(now, Line("PANS", 5), Line("MUG", 5), Line("CUP", 5));
        var first = ReturnRequest.Open(now, order,
        [
            new(order.Lines[0].VariantId, "PANS", 3, ReturnReason.Damaged, null),
            new(order.Lines[1].VariantId, "MUG", 1, ReturnReason.ChangedMind, null),
            new(order.Lines[2].VariantId, "CUP", 1, ReturnReason.WrongItem, null),
        ], earlier: []);

        var (summary, _) = Summarise(now, 2, first);

        Assert.Equal(3, summary.SkuTotal);
        Assert.Equal(["PANS", "CUP"], summary.BySku.Select(row => row.Sku));
        Assert.Equal(3, summary.BySku[0].ByReason.Single(row => row.Reason == ReturnReason.Damaged).Units);
    }

    /// <summary>A SKU sent back in two returns counts both, and so does a reason: the largest single line is not the figure.</summary>
    [Fact]
    public void Units_are_summed_over_every_return_of_a_sku_and_of_a_reason()
    {
        var now = new TestClock();
        var order = Delivered(now, Line("PANS", 5), Line("MUG", 5));
        var first = ReturnRequest.Open(now, order, [new(order.Lines[0].VariantId, "PANS", 3, ReturnReason.Damaged, null)], earlier: []);
        var second = ReturnRequest.Open(now, order,
        [
            new(order.Lines[0].VariantId, "PANS", 1, ReturnReason.Damaged, null),
            new(order.Lines[1].VariantId, "MUG", 2, ReturnReason.Damaged, null),
        ], earlier: [first]);

        var (summary, _) = Summarise(now, 5, first, second);

        Assert.Equal(4, summary.BySku.Single(row => row.Sku == "PANS").Units);
        Assert.Equal(4, summary.BySku.Single(row => row.Sku == "PANS").ByReason.Single(row => row.Reason == ReturnReason.Damaged).Units);
        Assert.Equal(6, summary.ByReason.Single(row => row.Reason == ReturnReason.Damaged).Units);
    }

    /// <summary>No order, no customer, no comment: a return's comment is the
    /// shopper's own words, and these figures may leave the machine (the spec).</summary>
    [Fact]
    public void The_summary_carries_no_order_customer_or_comment()
    {
        string[] forbidden = ["OrderId", "CustomerId", "Comment", "Email", "Address", "ReturnId"];
        var names = new[] { typeof(ReturnsSummary), typeof(ReasonUnits), typeof(SkuReturns) }
            .SelectMany(type => type.GetProperties()).Select(property => property.Name);

        Assert.Empty(names.Intersect(forbidden));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(ReadLimits.MaxRows, true)]
    [InlineData(ReadLimits.MaxRows + 1, false)]
    public void A_limit_is_between_one_and_fifty(int limit, bool valid) =>
        Assert.Equal(valid, new ReturnsSummaryValidator().Validate(new GetReturnsSummaryQuery(DashboardPeriod.Week, limit)).IsValid);

    private static Order Delivered(TestClock clock, params OrderLine[] lines) =>
        OrderBuilder.Default().WithLines(lines).At(clock).InStatus(OrderStatus.Delivered);
}
