using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The orders list's filter (the redesign's R-4.2), where only Postgres can
/// answer: an id matched by its start through the cast under <c>OrderId</c>,
/// a name and a city read out of the address's JSON and matched ignoring case,
/// and the counts by status taken after the text and before the status.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderListFilterTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Narrows_by_status_and_by_what_was_typed_and_counts_what_the_text_left()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("order_list_filter");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        // Three in Madrid and one in Sevilla; two of Madrid's confirmed. Lopsided
        // on purpose, so a count read from the wrong set cannot come out right.
        var anaConfirmed = Placed("Ana Ruiz", "Madrid", "a", confirm: true);
        var anaShipped = Placed("Ana Ruiz", "Madrid", "b", confirm: true);
        anaShipped.Ship(Clock);
        var luisConfirmed = Placed("Luis Gómez", "Madrid", "c", confirm: true);
        var martaPending = Placed("Marta 50%_off", "Sevilla", "d", confirm: false);

        await using (var context = factory.Create())
        {
            context.Orders.AddRange(anaConfirmed, anaShipped, luisConfirmed, martaPending);
            await context.SaveChangesAsync(ct);
        }

        var services = new ServiceCollection().AddLogging().AddTenderoPersistence(factory.ConnectionString).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IOrderReader>();

        var everything = await reader.ListAsync(new OrderListQuery(null, null), ct);
        Assert.Equal(4, everything.Orders.Count);
        Assert.Equal(2, everything.CountByStatus[OrderStatus.Confirmed]);

        var toShip = await reader.ListAsync(new OrderListQuery(OrderStatus.Confirmed, null), ct);
        Assert.Equal(
            new[] { anaConfirmed.Id, luisConfirmed.Id }.OrderBy(id => id.Value),
            toShip.Orders.Select(o => o.Id).OrderBy(id => id.Value));
        // The counts ignore the status filter: a chip still says how many are Shipped.
        Assert.Equal(1, toShip.CountByStatus[OrderStatus.Shipped]);

        var ana = await reader.ListAsync(new OrderListQuery(null, "ana ruiz"), ct);
        Assert.Equal(2, ana.Orders.Count);
        Assert.Equal(1, ana.CountByStatus[OrderStatus.Confirmed]);
        Assert.Equal(1, ana.CountByStatus[OrderStatus.Shipped]);

        var madridToShip = await reader.ListAsync(new OrderListQuery(OrderStatus.Confirmed, "MADRID"), ct);
        Assert.Equal(2, madridToShip.Orders.Count);

        // A name or a city is found with or without its accent, typed either way:
        // "gomez" is how Gómez is searched for, and "Sevílla" is a slip.
        Assert.Equal(luisConfirmed.Id, Assert.Single((await reader.ListAsync(new OrderListQuery(null, "gomez"), ct)).Orders).Id);
        Assert.Equal(luisConfirmed.Id, Assert.Single((await reader.ListAsync(new OrderListQuery(null, "LUIS GÓMEZ"), ct)).Orders).Id);
        Assert.Equal(martaPending.Id, Assert.Single((await reader.ListAsync(new OrderListQuery(null, "sevílla"), ct)).Orders).Id);

        // The whole id, typed in capitals. Not its first eight characters: an id
        // is a version-7 Guid, whose start is a timestamp, and the four orders
        // here were placed in the same instant, so they share it.
        var byId = await reader.ListAsync(new OrderListQuery(null, luisConfirmed.Id.ToString().ToUpperInvariant()), ct);
        Assert.Equal(luisConfirmed.Id, Assert.Single(byId.Orders).Id);

        // Its random end tells it apart where its start cannot.
        var byEnd = await reader.ListAsync(new OrderListQuery(null, luisConfirmed.Id.ToString()[^8..]), ct);
        Assert.Equal(luisConfirmed.Id, Assert.Single(byEnd.Orders).Id);
        Assert.Equal(4, (await reader.ListAsync(new OrderListQuery(null, luisConfirmed.Id.ToString()[..8]), ct)).Orders.Count);

        // A % or an _ typed is a character, not a wildcard: as a wildcard,
        // "ana%ruiz" would find Ana Ruiz's two orders.
        Assert.Equal(martaPending.Id, Assert.Single((await reader.ListAsync(new OrderListQuery(null, "50%_"), ct)).Orders).Id);
        Assert.Empty((await reader.ListAsync(new OrderListQuery(null, "ana%ruiz"), ct)).Orders);
    }

    /// <summary>
    /// The list pages (R-7.2), and only Postgres can say whether its order is
    /// stable: five orders placed in the same instant tie on their time, and the
    /// id is what keeps page two from repeating or skipping one of them. The
    /// newest, placed a minute later, still comes first.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Page_two_continues_page_one_through_orders_placed_in_the_same_instant()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("order_list_paging");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        var clock = new TestClock();
        var tied = Enumerable.Range(0, 5)
            .Select(i => OrderBuilder.Default().At(clock).WithIdempotencyKey($"list-paging-{i}").Build())
            .ToList();
        foreach (var order in tied.Take(3))
        {
            order.AuthorizePayment(clock, new OrderPayment("fake", $"auth-{order.Id}", null));
            order.CapturePayment(clock, $"capture-{order.Id}");
            order.Confirm(clock);
        }

        clock.Advance(TimeSpan.FromMinutes(1));
        var newest = OrderBuilder.Default().At(clock).WithIdempotencyKey("list-paging-newest").Build();

        await using (var context = factory.Create())
        {
            context.Orders.AddRange([.. tied, newest]);
            await context.SaveChangesAsync(ct);
        }

        var services = new ServiceCollection().AddLogging().AddTenderoPersistence(factory.ConnectionString).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IOrderReader>();

        var pages = new List<OrderList>();
        for (var page = 1; page <= 3; page++)
            pages.Add(await reader.ListAsync(new OrderListQuery(null, null, page, 2), ct));

        // Newest first, then the tie in the id's order: Postgres orders a uuid by
        // its bytes, which is the order of its lower-case text.
        OrderId[] expected = [newest.Id, .. tied.Select(order => order.Id).OrderBy(id => id.ToString(), StringComparer.Ordinal)];
        Assert.Equal(expected, pages.SelectMany(page => page.Orders).Select(order => order.Id));
        Assert.All(pages, page => Assert.Equal(6, page.Total));

        // The total is the status's, not the page's: three confirmed, one on page two.
        var confirmed = await reader.ListAsync(new OrderListQuery(OrderStatus.Confirmed, null, 2, 2), ct);
        Assert.Single(confirmed.Orders);
        Assert.Equal(3, confirmed.Total);
        Assert.Empty((await reader.ListAsync(new OrderListQuery(null, null, 4, 2), ct)).Orders);
    }

    private static Order Placed(string recipient, string city, string key, bool confirm)
    {
        var order = OrderBuilder.Default()
            .WithIdempotencyKey($"list-filter-{key}")
            .ShippedTo(Address.Create(recipient, "Calle Mayor 1", null, city, null, "28013", "ES"))
            .Build();
        if (!confirm) return order;

        order.AuthorizePayment(Clock, new OrderPayment("fake", $"auth-{key}", null));
        order.CapturePayment(Clock, $"capture-{key}");
        order.Confirm(Clock);
        return order;
    }
}
