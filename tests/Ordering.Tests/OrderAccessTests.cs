using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.GetOrder;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// Who may read an order by its id.
///
/// The id was the only credential, by a decision written beside the endpoint:
/// "a guest has to be able to reload their confirmation page, and until phase 7
/// there is no account to scope it to … it stops being true the moment Accounts
/// exists. The line to change is this one." Accounts shipped on 2026-09-04 and
/// the line did not change, so the UI review of 2026-09-11 read a shopper's name
/// and address with nothing but a URL.
///
/// A guest's order keeps the id as its credential — there is still no account to
/// scope it to. An order that belongs to an account is that account's, and the
/// shopkeeper's; to anybody else it does not exist.
/// </summary>
public sealed class OrderAccessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CommercePrincipal Stranger = CommercePrincipal.Anonymous;

    [Fact]
    public async Task A_guests_order_is_read_with_its_id_alone()
    {
        var order = OrderBuilder.Default().Build();

        Assert.NotNull(await Read(order, registered: false, Stranger));
    }

    [Fact]
    public async Task An_account_holders_order_does_not_exist_for_a_stranger()
    {
        var order = OrderBuilder.Default().Build();

        Assert.Null(await Read(order, registered: true, Stranger));
    }

    [Fact]
    public async Task An_account_holder_reads_their_own_order()
    {
        var order = OrderBuilder.Default().Build();
        var owner = new CommercePrincipal(order.CustomerId, null, "ana", "Ana Ruiz", [TenderoRoles.Shopper]);

        Assert.NotNull(await Read(order, registered: true, owner));
    }

    /// <summary>Signed in is not enough: another shopper's account is a
    /// stranger to this order.</summary>
    [Fact]
    public async Task Another_shopper_is_a_stranger_to_an_order_that_is_not_theirs()
    {
        var order = OrderBuilder.Default().Build();
        var other = new CommercePrincipal(CustomerId.New(), null, "eve", "Eve", [TenderoRoles.Shopper]);

        Assert.Null(await Read(order, registered: true, other));
    }

    [Fact]
    public async Task The_shopkeeper_reads_any_order()
    {
        var order = OrderBuilder.Default().Build();
        var shopkeeper = new CommercePrincipal(null, null, "juanlu", "Juan Luis", [TenderoRoles.Shopkeeper]);

        Assert.NotNull(await Read(order, registered: true, shopkeeper));
    }

    /// <summary>Opening a return is reaching the order through its id too, so a
    /// stranger meets the same answer: there is no such order.</summary>
    [Fact]
    public async Task A_stranger_cannot_open_a_return_on_an_account_holders_order()
    {
        var order = OrderBuilder.Default().InStatus(OrderStatus.Delivered);

        var handler = new ElGuerre.Tendero.Ordering.Features.Returns.RequestReturnHandler(
            new OneOrder(order),
            new NoReturns(),
            new NothingSaved(),
            new TestClock(),
            new OrderAccess(new Accounts(registered: true), new Fixed(Stranger)));

        var result = await handler.HandleAsync(
            new ElGuerre.Tendero.Ordering.Features.Returns.RequestReturnCommand(
                order.Id, [new(order.Lines[0].Sku, 1, nameof(ReturnReason.WrongSize), null)], "es"),
            Ct);

        Assert.Equal(ElGuerre.Tendero.Ordering.Features.Returns.RequestReturnOutcome.UnknownOrder, result.Outcome);
    }

    private sealed class NothingSaved : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static Task<OrderDetail?> Read(Order order, bool registered, CommercePrincipal principal)
    {
        var handler = new GetOrderHandler(
            new OneOrder(order),
            new NoReturns(),
            new OrderAccess(new Accounts(registered), new Fixed(principal)));

        return handler.HandleAsync(new GetOrderQuery(order.Id), Ct);
    }

    private sealed class OneOrder(Order order) : IOrderRepository
    {
        public Task<Order?> FindByIdAsync(OrderId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Order?>(id == order.Id ? order : null);

        public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<Order?>(null);

        public void Add(Order order) { }
    }

    private sealed class NoReturns : IReturnRequestRepository
    {
        public Task<ReturnRequest?> FindByIdAsync(ReturnRequestId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReturnRequest?>(null);

        public Task<IReadOnlyList<ReturnRequest>> ForOrderAsync(OrderId orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReturnRequest>>([]);

        public Task<IReadOnlyList<ReturnRequest>> OpenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReturnRequest>>([]);

        public void Add(ReturnRequest request) { }
    }

    private sealed class Accounts(bool registered) : IRegisteredCustomers
    {
        public Task<bool> HasAccountAsync(CustomerId customer, CancellationToken cancellationToken = default) =>
            Task.FromResult(registered);
    }

    private sealed class Fixed(CommercePrincipal principal) : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = principal;
    }
}
