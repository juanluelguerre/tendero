using System.Runtime.CompilerServices;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.ImportOrders;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Ordering.Tests.Sources;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>An order source written in memory: the slice's control.</summary>
internal sealed class FakeOrderSource : IOrderSourceConnector
{
    public const string Key = "fake";

    private static readonly Address Ana = Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");
    public static readonly DateTimeOffset PlacedThen = new(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);

    private static Money Eur(decimal amount) => new(amount, "EUR");

    public static SourceOrder Shirts(string id, string number, SourceOrderState state = SourceOrderState.Fulfilled, string? customer = "6001",
        string sku = "SHIRT-M", IReadOnlyList<SourceRefund>? refunds = null, decimal total = 40m) =>
        new(id, number, customer, "ana@example.com", "es", PlacedThen, state == SourceOrderState.Fulfilled ? PlacedThen.AddDays(1) : null, state,
            [new SourceOrderLine($"{id}-1", sku, "Camiseta", "M", Eur(20m), 2)],
            Ana, Ana, new SourceShipping("Envío estándar", Eur(0m)),
            new SourceOrderTotals(Eur(40m), Eur(0m), Eur(0m), Eur(6.94m), Eur(total)),
            [], [new SourceTax("IVA 21%", 0.21m, Eur(6.94m))], refunds ?? []);

    public List<SourceOrder> Orders { get; } =
    [
        Shirts("5001", "#1001"),
        Shirts("5002", "#1002", state: SourceOrderState.Cancelled),
        Shirts("5003", "#1003", refunds: [new SourceRefund("7001", PlacedThen.AddDays(10), Eur(20m), [("5003-1", 1)])]),
    ];

    public string Source => Key;

    public async IAsyncEnumerable<SourceOrder> StreamOrdersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        foreach (var order in Orders)
            yield return order;
    }
}

/// <summary>A store's gift cards written in memory: Ana's, which expires, and one with no holder.</summary>
internal sealed class FakeGiftCardSource : IGiftCardSourceConnector
{
    public List<SourceGiftCard> Cards { get; } =
    [
        new("8001", "ab12", new Money(25m, "EUR"), new DateOnly(2027, 12, 31), "6001"),
        new("8002", "cd34", new Money(50m, "EUR"), null, null),
    ];

    public string Source => FakeOrderSource.Key;

    public async IAsyncEnumerable<SourceGiftCard> StreamGiftCardsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        foreach (var card in Cards)
            yield return card;
    }
}

/// <summary>The fake gift card source passes the contract every gift card source does.</summary>
public sealed class FakeGiftCardSourceContractTests : GiftCardSourceConnectorContractTests
{
    protected override IGiftCardSourceConnector CreateConnector() => new FakeGiftCardSource();
}

/// <summary>The fake order source passes the contract every order source does.</summary>
public sealed class FakeOrderSourceContractTests : OrderSourceConnectorContractTests
{
    protected override IOrderSourceConnector CreateConnector() => new FakeOrderSource();
}

/// <summary>
/// Bringing a store's order history (phase 16, S2b, P16-18; ADR 0066): each closed
/// order born in its final state, its lines resolved to the catalogue by SKU with
/// the names the buyer saw, owned by the customer Accounts imported for the buyer,
/// and each refund a return born refunded. Open orders are finished in the store
/// first; an order the catalogue cannot place is not invented.
/// </summary>
public sealed class ImportOrdersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly CustomerId Ana = CustomerId.New();

    private sealed class Shop
    {
        public FakeOrderSource Source { get; } = new();

        public Orders Orders { get; } = new();

        public Returns Returns { get; } = new();

        public CountingUnitOfWork UnitOfWork { get; } = new();

        /// <summary>The source's gift cards, or null for a source that registers none.</summary>
        public FakeGiftCardSource? GiftCards { get; set; } = new();

        public Task<ImportOrdersResult> ImportAsync(bool rehearse = false, string culture = "es") =>
            new ImportOrdersHandler(new OneSource(Source), new GiftCardsOf(GiftCards), Orders, Returns, new Catalogue(), new ImportedAna(), UnitOfWork)
                .HandleAsync(new ImportOrdersCommand(FakeOrderSource.Key, culture, rehearse), Ct);
    }

    /// <summary>
    /// A gift card cannot come across (decision 6): each one still owed is listed with
    /// the end of its code, its balance, its expiry and whom it belongs to here, for
    /// the merchant to reissue.
    /// </summary>
    [Fact]
    public async Task Each_gift_card_still_owed_is_listed_for_the_merchant_to_reissue()
    {
        var shop = new Shop();

        var result = await shop.ImportAsync();

        Assert.Equal(
            [
                new GiftCardToReissue("ab12", new Money(25m, "EUR"), new DateOnly(2027, 12, 31), Ana.Value.ToString(), "6001"),
                new GiftCardToReissue("cd34", new Money(50m, "EUR"), null, null, null),
            ],
            result.GiftCards);
    }

    /// <summary>A holder the customers' import did not bring is still named, by the store's id for them.</summary>
    [Fact]
    public async Task A_gift_card_whose_holder_was_not_imported_names_them_as_the_store_did()
    {
        var shop = new Shop();
        shop.GiftCards!.Cards.Add(new SourceGiftCard("8003", "ef56", new Money(10m, "EUR"), null, "6999"));

        var result = await shop.ImportAsync();

        Assert.Equal(new GiftCardToReissue("ef56", new Money(10m, "EUR"), null, null, "6999"), result.GiftCards[^1]);
    }

    [Fact]
    public async Task A_rehearsal_lists_the_gift_cards_too()
    {
        var shop = new Shop();

        var rehearsal = await shop.ImportAsync(rehearse: true);

        Assert.Equal(["ab12", "cd34"], rehearsal.GiftCards.Select(card => card.LastCharacters));
    }

    [Fact]
    public async Task A_source_with_no_gift_cards_lists_none()
    {
        var shop = new Shop { GiftCards = null };

        var result = await shop.ImportAsync();

        Assert.Equal(3, result.Created);
        Assert.Empty(result.GiftCards);
    }

    [Fact]
    public async Task Each_closed_order_is_born_in_its_final_state_owned_by_its_imported_customer()
    {
        var shop = new Shop();

        var result = await shop.ImportAsync();

        Assert.Equal((3, 0, 1), (result.Created, result.AlreadyImported, result.Refunds));
        var delivered = shop.Orders.All.Single(order => order.ImportedFrom!.Number == "#1001");
        Assert.Equal((OrderStatus.Delivered, Ana, FakeOrderSource.PlacedThen), (delivered.Status, delivered.CustomerId, delivered.CreatedAt));
        Assert.Equal(OrderStatus.Cancelled, shop.Orders.All.Single(order => order.ImportedFrom!.Number == "#1002").Status);

        var line = Assert.Single(delivered.Lines);
        Assert.Equal((Catalogue.Product, Catalogue.Variant, "SHIRT-M", "Camiseta", "M", 2), (line.ProductId, line.VariantId, line.Sku, line.ProductName, line.VariantLabel, line.Quantity));
        Assert.Equal(("HOME", "HOME"), (line.CategoryCode, line.CategoryPath));
        Assert.Equal(new OrderTax("IVA 21%", 0.21m, new Money(33.05m, "EUR"), new Money(6.94m, "EUR")), Assert.Single(delivered.Taxes));
        Assert.Equal(1, shop.UnitOfWork.Saves);
    }

    [Fact]
    public async Task A_refund_is_a_return_born_refunded_with_no_reason_stated()
    {
        var shop = new Shop();

        await shop.ImportAsync();

        var refund = Assert.Single(shop.Returns.All);
        Assert.Equal((ReturnStatus.Refunded, new Money(20m, "EUR"), "fake:refund:7001"), (refund.Status, refund.RefundAmount, refund.RefundReference));
        Assert.Equal((ReturnReason.Unstated, 1), (Assert.Single(refund.Lines).Reason, refund.Lines[0].Quantity));
    }

    [Fact]
    public async Task A_second_import_brings_no_order_twice()
    {
        var shop = new Shop();
        await shop.ImportAsync();

        var again = await shop.ImportAsync();

        Assert.Equal((0, 3), (again.Created, again.AlreadyImported));
        Assert.Equal(3, shop.Orders.All.Count);
        Assert.Single(shop.Returns.All);
    }

    /// <summary>An order still on its way is finished in the store, not brought half-walked.</summary>
    [Fact]
    public async Task An_open_order_is_said_and_not_imported()
    {
        var shop = new Shop();
        shop.Source.Orders.Add(FakeOrderSource.Shirts("5004", "#1004", state: SourceOrderState.Open));

        var result = await shop.ImportAsync();

        Assert.Equal(3, result.Created);
        Assert.Contains(result.Notes, note => note.Code == OrderNoteCode.StillOpen && note.About == "#1004");
    }

    /// <summary>A line the catalogue does not have is not invented: the order waits for the catalogue's import.</summary>
    [Fact]
    public async Task An_order_naming_something_the_catalogue_lacks_is_said_and_not_imported()
    {
        var shop = new Shop();
        shop.Source.Orders.Add(FakeOrderSource.Shirts("5005", "#1005", sku: "GONE-SKU"));

        var result = await shop.ImportAsync();

        Assert.DoesNotContain(shop.Orders.All, order => order.ImportedFrom!.Number == "#1005");
        Assert.Contains(result.Notes, note => note.Code == OrderNoteCode.ProductNotInCatalogue && note.About == "#1005");
    }

    /// <summary>A buyer the customers' import did not bring: the order comes, owned by a guest, and it is said.</summary>
    [Fact]
    public async Task An_order_whose_buyer_was_not_imported_belongs_to_a_guest()
    {
        var shop = new Shop();
        shop.Source.Orders.Add(FakeOrderSource.Shirts("5006", "#1006", customer: "6999"));

        var result = await shop.ImportAsync();

        var guest = shop.Orders.All.Single(order => order.ImportedFrom!.Number == "#1006");
        Assert.NotEqual(Ana, guest.CustomerId);
        Assert.Contains(result.Notes, note => note.Code == OrderNoteCode.CustomerNotImported && note.About == "#1006");
    }

    /// <summary>Figures that do not add up as an order's must are refused, not stored.</summary>
    [Fact]
    public async Task An_order_whose_figures_do_not_add_up_is_said_and_not_imported()
    {
        var shop = new Shop();
        shop.Source.Orders.Add(FakeOrderSource.Shirts("5007", "#1007", total: 99m));

        var result = await shop.ImportAsync();

        Assert.Contains(result.Notes, note => note.Code == OrderNoteCode.TotalsDoNotAddUp && note.About == "#1007");
    }

    [Fact]
    public async Task A_rehearsal_says_what_would_come_and_writes_nothing()
    {
        var shop = new Shop();
        shop.Source.Orders.Add(FakeOrderSource.Shirts("5004", "#1004", state: SourceOrderState.Open));

        var rehearsal = await shop.ImportAsync(rehearse: true, culture: "en");

        Assert.Equal((3, 1), (rehearsal.Created, rehearsal.Refunds));
        Assert.Empty(shop.Orders.All);
        Assert.Empty(shop.Returns.All);
        Assert.Equal(0, shop.UnitOfWork.Saves);
        Assert.StartsWith("#1004: ", Assert.Single(rehearsal.Notes).Text, StringComparison.Ordinal);
    }

    private sealed class OneSource(IOrderSourceConnector source) : IOrderSourceRegistry
    {
        public IReadOnlyCollection<string> Sources => [source.Source];

        public IOrderSourceConnector Get(string name) => source;
    }

    private sealed class GiftCardsOf(IGiftCardSourceConnector? source) : IGiftCardSourceRegistry
    {
        public IGiftCardSourceConnector? Find(string name) => source?.Source == name ? source : null;
    }

    private sealed class Orders : IOrderRepository
    {
        public List<Order> All { get; } = [];

        public Task<Order?> FindByIdAsync(OrderId id, CancellationToken cancellationToken = default) => Task.FromResult(All.Find(order => order.Id == id));

        public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(All.Find(order => order.IdempotencyKey == key));

        public void Add(Order order) => All.Add(order);
    }

    private sealed class Returns : IReturnRequestRepository
    {
        public List<ReturnRequest> All { get; } = [];

        public Task<ReturnRequest?> FindByIdAsync(ReturnRequestId id, CancellationToken cancellationToken = default) => Task.FromResult(All.Find(each => each.Id == id));

        public Task<IReadOnlyList<ReturnRequest>> ForOrderAsync(OrderId orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReturnRequest>>([.. All.Where(each => each.OrderId == orderId)]);

        public Task<IReadOnlyList<ReturnRequest>> OpenAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReturnRequest>>([]);

        public void Add(ReturnRequest request) => All.Add(request);
    }

    /// <summary>The catalogue sells one shirt under SHIRT-M.</summary>
    private sealed class Catalogue : IPurchasableReader
    {
        public static readonly ProductId Product = ProductId.New();
        public static readonly VariantId Variant = VariantId.New();

        public Task<IReadOnlyDictionary<string, PurchasableVariant>> FindBySkusAsync(IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, PurchasableVariant>>(skus
                .Where(sku => sku == "SHIRT-M")
                .ToDictionary(sku => sku, sku => new PurchasableVariant(
                    Product, Variant, sku, LocalizedText.From("es", "Camiseta del catálogo"), null, null, "EUR", "HOME", "HOME")));
    }

    /// <summary>Accounts imported store customer 6001 as Ana; nobody else.</summary>
    private sealed class ImportedAna : IImportedCustomerIds
    {
        public Task<IReadOnlyDictionary<string, CustomerId>> ForAsync(string source, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, CustomerId>>(externalIds.Where(id => id == "6001").ToDictionary(id => id, _ => Ana));
    }
}
