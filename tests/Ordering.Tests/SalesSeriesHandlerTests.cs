using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>The series' handler asks the shop history for both periods at once, and buckets them on the shop's clock (ADR 0046).</summary>
public sealed class SalesSeriesHandlerTests
{
    private sealed class RecordingHistory(IReadOnlyList<Order> orders) : IShopHistory
    {
        public (DateTimeOffset From, DateTimeOffset To)? Asked { get; private set; }

        public Task<IReadOnlyList<Order>> OrdersPlacedAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            Asked = (from, to);
            return Task.FromResult(orders);
        }

        public Task<IReadOnlyList<Order>> OrdersInAsync(IReadOnlyCollection<OrderStatus> statuses, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Cart>> CartsStartedAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReturnRequest>> ReturnsOpenOrChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<CustomerId>> CustomersWhoOrderedBeforeAsync(IReadOnlyCollection<CustomerId> customers, DateTimeOffset before, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task It_reads_both_periods_in_one_request_and_answers_on_the_shops_clock()
    {
        var now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        var history = new RecordingHistory([]);
        var handler = new SalesSeriesHandler(history, new TestClock(now), Options.Create(new ShopOptions { TimeZone = "Europe/Madrid" }));

        var series = await handler.HandleAsync(new GetSalesSeriesQuery(DashboardPeriod.Week), TestContext.Current.CancellationToken);

        Assert.Equal("Europe/Madrid", series.TimeZone);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(2)), history.Asked?.From);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.FromHours(2)), history.Asked?.To);
    }
}
