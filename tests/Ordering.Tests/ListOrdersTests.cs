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
