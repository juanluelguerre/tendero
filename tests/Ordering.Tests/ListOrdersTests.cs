using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.ListOrders;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// The orders list's slice (the redesign's R-4.2). The filtering itself is the
/// database's and is held by <c>OrderListFilterTests</c> against Postgres; what
/// is the slice's is passing the filter through and saying a count for every
/// status, zero included, so a filter chip never vanishes.
/// </summary>
public sealed class ListOrdersTests
{
    [Fact]
    public async Task Passes_the_filter_to_the_reader_as_it_came()
    {
        var reader = new ScriptedReader([], new Dictionary<OrderStatus, int>());

        await new ListOrdersHandler(reader).HandleAsync(new ListOrdersQuery(OrderStatus.Confirmed, "madrid"), TestContext.Current.CancellationToken);

        Assert.Equal(new OrderListQuery(OrderStatus.Confirmed, "madrid"), reader.Asked);
    }

    [Fact]
    public async Task Counts_every_status_including_the_ones_nothing_matched()
    {
        var reader = new ScriptedReader(
            [OrderBuilder.Default().Build()],
            new Dictionary<OrderStatus, int> { [OrderStatus.Confirmed] = 3, [OrderStatus.Shipped] = 1 });

        var result = await new ListOrdersHandler(reader).HandleAsync(new ListOrdersQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(Enum.GetNames<OrderStatus>(), result.Counts.Select(count => count.Status));
        Assert.Equal(3, result.Counts.Single(count => count.Status == "Confirmed").Count);
        Assert.Equal(1, result.Counts.Single(count => count.Status == "Shipped").Count);
        Assert.Equal(0, result.Counts.Single(count => count.Status == "Cancelled").Count);
        Assert.Single(result.Orders);
    }

    [Fact]
    public void A_row_tells_authorised_from_captured()
    {
        var unpaid = OrderBuilder.Default().Build();
        var authorised = OrderBuilder.Default().Build();
        authorised.AuthorizePayment(TimeProvider.System, new OrderPayment("fake", "auth-1", null));
        var captured = OrderBuilder.Default().Build();
        captured.AuthorizePayment(TimeProvider.System, new OrderPayment("fake", "auth-2", null));
        captured.CapturePayment(TimeProvider.System, "cap-2");

        Assert.Equal((false, false), Paid(OrderSummary.Of(unpaid)));
        Assert.Equal((true, false), Paid(OrderSummary.Of(authorised)));
        Assert.Equal((true, true), Paid(OrderSummary.Of(captured)));

        static (bool, bool) Paid(OrderSummary row) => (row.IsPaid, row.IsCaptured);
    }

    [Fact]
    public void A_row_previews_the_first_three_lines_in_their_order()
    {
        var lines = Enumerable.Range(1, 4).Select(_ => OrderBuilder.Line(10m, "EUR")).ToArray();

        var row = OrderSummary.Of(OrderBuilder.Default().WithLines(lines).Build());

        Assert.Equal(lines.Take(3).Select(line => line.Sku), row.Preview.Select(preview => preview.Sku));
        Assert.Equal(4, row.LineCount);
    }

    private sealed class ScriptedReader(IReadOnlyList<Order> orders, IReadOnlyDictionary<OrderStatus, int> counts) : IOrderReader
    {
        public OrderListQuery? Asked { get; private set; }

        public Task<OrderList> ListAsync(OrderListQuery query, CancellationToken cancellationToken = default)
        {
            Asked = query;
            return Task.FromResult(new OrderList(orders, counts));
        }

        public Task<IReadOnlyList<Order>> ForCustomerAsync(ElGuerre.Tendero.SharedKernel.CustomerId customer, CancellationToken cancellationToken = default) =>
            Task.FromResult(orders);
    }
}
