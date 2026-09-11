using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Returns;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// The losing side of two returns racing for the last unit.
///
/// The database refuses it (the unique index on order and return number, proven
/// against Postgres in <c>CheckoutLoopTests</c>); what is asserted here is that
/// the shopper — or the agent — hears a refusal they can act on, in the shape
/// every other refusal of this command has, and not a 500.
/// </summary>
public sealed class RequestReturnRaceTests
{
    [Fact]
    public async Task Losing_a_race_for_the_last_unit_is_a_refusal_and_not_a_fault()
    {
        var order = OrderBuilder.Default().InStatus(OrderStatus.Delivered);

        var handler = new RequestReturnHandler(
            new OneOrder(order),
            new NoEarlierReturns(),
            new SomebodyGotThereFirst(),
            new TestClock(),
            new OrderAccess(new NoAccounts(), new Guest()));

        var result = await handler.HandleAsync(
            new RequestReturnCommand(order.Id, [new(order.Lines[0].Sku, 1, nameof(ReturnReason.WrongSize), null)], "es"),
            TestContext.Current.CancellationToken);

        Assert.Equal(RequestReturnOutcome.Refused, result.Outcome);
        Assert.Contains("at the same moment", result.Detail);
    }

    private sealed class SomebodyGotThereFirst : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken ct) =>
            throw new ConcurrentWriteException("ux_return_requests_order_id_number", new InvalidOperationException());
    }

    private sealed class OneOrder(Order order) : IOrderRepository
    {
        public Task<Order?> FindByIdAsync(OrderId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Order?>(order);

        public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<Order?>(null);

        public void Add(Order order) { }
    }

    private sealed class NoEarlierReturns : IReturnRequestRepository
    {
        public Task<ReturnRequest?> FindByIdAsync(ReturnRequestId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReturnRequest?>(null);

        public Task<IReadOnlyList<ReturnRequest>> ForOrderAsync(OrderId orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReturnRequest>>([]);

        public Task<IReadOnlyList<ReturnRequest>> OpenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReturnRequest>>([]);

        public void Add(ReturnRequest request) { }
    }

    private sealed class NoAccounts : IRegisteredCustomers
    {
        public Task<bool> HasAccountAsync(CustomerId customer, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class Guest : IPrincipalAccessor
    {
        public CommercePrincipal Current => CommercePrincipal.Anonymous;
    }
}
