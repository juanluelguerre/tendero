using System.Text.Json;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Features.ListCampaigns;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// What Tino may read of the shop's offers (phase 13c). Asked "¿alguna oferta
/// para hoy?" it answered that it had no such data, while a campaign was
/// running and 32 products were on offer: none of its reads said what the shop
/// runs. This is the home's own query, said as sentences Tino can cite.
/// </summary>
public sealed class ListCampaignsToolTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Shop(params CampaignView[] campaigns) : IQueryDispatcher
    {
        public ListCampaignsQuery? Asked { get; private set; }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = (ListCampaignsQuery)(object)query;
            return Task.FromResult((TResult)(object)new ListCampaignsResult(campaigns));
        }
    }

    /// <summary>Ends at midnight in Madrid on 1 December: its last day is 30 November.</summary>
    private static readonly CampaignView Autumn = new(
        "OTONO", "Otoño en Tendero", "La temporada en ropa", CampaignKind.Season, CampaignScope.Shop, null, CampaignPhase.Running,
        new DateTimeOffset(2026, 9, 21, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 30, 23, 0, 0, TimeSpan.Zero), 1,
        [
            new CampaignHold("APPAREL10", "10 % en ropa", null), new CampaignHold("OTONO5", "5 % extra", "OTONO5"),
            new CampaignHold("KITCHEN5", "5 € en menaje", null), new CampaignHold("SHIRT3X2", "3x2 en camisetas", null),
        ],
        null, null);

    private static readonly CampaignView BlackFriday = new(
        "BF", "Black Friday", "Una semana", CampaignKind.Event, CampaignScope.Shop, null, CampaignPhase.Upcoming,
        new DateTimeOffset(2026, 11, 26, 23, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 2, 23, 0, 0, TimeSpan.Zero), 2,
        [new CampaignHold("SHIP0", "Envío gratis", null), new CampaignHold("GONE", "20 % en todo", null)], null, null);

    /// <summary>The promotions the holds name, as the shop keeps them; GONE is not one of them.</summary>
    private sealed class Promotions : IPromotionReader
    {
        public Task<IReadOnlyList<Promotion>> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Promotion>>(
            [
                Promotion("APPAREL10", new PercentOffLine(10), new PromotionCondition(CategoryCode: "APPAREL")),
                Promotion("OTONO5", new PercentOffLine(5), PromotionCondition.Always),
                Promotion("KITCHEN5", new AmountOffOrder(new Money(5, "EUR")),
                    new PromotionCondition(MinimumSubtotal: new Money(40, "EUR"), CategoryCode: "KITCHEN")),
                Promotion("SHIRT3X2", new BuyXGetY(2, 1), new PromotionCondition(CategoryCode: "SHIRT", MinimumQuantity: 3)),
                Promotion("SHIP0", new FreeShipping(), new PromotionCondition(MinimumSubtotal: new Money(50, "EUR"))),
            ]);

        private static Promotion Promotion(string code, PromotionEffect effect, PromotionCondition condition) =>
            new(code, new LocalizedText(new Dictionary<string, string> { ["es"] = code }), condition, effect, CombinationPolicy.Stackable, 1);
    }

    private static ListCampaignsTool Tool(Shop shop) =>
        new(shop, new Promotions(), Options.Create(new ShopOptions { TimeZone = "Europe/Madrid" }));

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void It_is_a_shoppers_read()
    {
        var tool = Tool(new Shop());

        Assert.Equal((ToolAudience.Shopper, "list_campaigns"), (tool.Audience, tool.Name));
    }

    /// <summary>
    /// A campaign running says what it holds and its last day, counted in the
    /// shop's day; one about to start, the day it starts. A promotion behind a
    /// coupon says the code to type. Each promotion says what it takes off and what
    /// it needs: "5 € en menaje" is 5 € off the ORDER, once it has an item in the promotion
    /// and comes to 40 € — said only by name, Tino could not tell a shopper what
    /// "esos 5 €" needed (2026-10-14). A promotion the shop no longer has is said by
    /// its name alone.
    /// </summary>
    [Fact]
    public async Task What_the_shop_runs_is_said_with_its_days_in_the_shops_zone()
    {
        var shop = new Shop(Autumn, BlackFriday);

        var answer = await Tool(shop).ReadAsync(Arguments("""{"culture":"es"}"""), Ct);

        Assert.Equal("es", shop.Asked!.Culture);
        Assert.Equal(
            [
                "«Otoño en Tendero» está en marcha hasta el 30 de noviembre de 2026: «10 % en ropa», un 10 % menos en cada artículo de la promoción; "
                + "«5 % extra», un 5 % menos en cada artículo de la promoción, con el código OTONO5; "
                + "«5 € en menaje», 5,00 € menos en el pedido si lleva algún artículo de la promoción y suma al menos 40,00 €; "
                + "«3x2 en camisetas», llévate 3 y paga 2.",
                "«Black Friday» empieza el 27 de noviembre de 2026: «Envío gratis», envío gratis si el pedido suma al menos 50,00 €; «20 % en todo».",
            ],
            answer.Facts!.Select(fact => fact.Text));
        Assert.All(answer.Facts!, fact => Assert.Equal((Fact.TheShop, FactSource.Campaign), (fact.Subject, fact.Source)));
    }

    [Fact]
    public async Task It_is_said_in_english_when_asked_to()
    {
        var answer = await Tool(new Shop(Autumn)).ReadAsync(Arguments("""{"culture":"en"}"""), Ct);

        Assert.Equal(
            [
                "“Otoño en Tendero” is running until 30 November 2026: “10 % en ropa”, 10% off each item in it; "
                + "“5 % extra”, 5% off each item in it, with the code OTONO5; "
                + "“5 € en menaje”, €5.00 off the order when it has an item in the promotion and comes to at least €40.00; "
                + "“3x2 en camisetas”, take 3 and pay for 2.",
            ],
            answer.Facts!.Select(fact => fact.Text));
    }

    /// <summary>In English as in Spanish: one about to start says the day it starts; one holding nothing says only its days.</summary>
    [Fact]
    public async Task One_about_to_start_or_holding_nothing_is_said_in_english_too()
    {
        var empty = Autumn with { Name = "Quiet week", Holds = [] };

        var answer = await Tool(new Shop(BlackFriday, empty)).ReadAsync(Arguments("""{"culture":"en"}"""), Ct);

        Assert.Equal(
            [
                "“Black Friday” starts on 27 November 2026: “Envío gratis”, free shipping when the order comes to at least €50.00; “20 % en todo”.",
                "“Quiet week” is running until 30 November 2026.",
            ],
            answer.Facts!.Select(fact => fact.Text));
        Assert.Equal(
            ["The shop has no campaign running or about to start."],
            (await Tool(new Shop()).ReadAsync(Arguments("""{"culture":"en"}"""), Ct)).Facts!.Select(fact => fact.Text));
    }

    /// <summary>Nothing running is an answer too: Tino can say so, rather than that it does not know.</summary>
    [Fact]
    public async Task A_shop_running_nothing_says_so()
    {
        var answer = await Tool(new Shop()).ReadAsync(Arguments("""{"culture":"es"}"""), Ct);

        Assert.Equal(["La tienda no tiene ninguna campaña en marcha ni a punto de empezar."], answer.Facts!.Select(fact => fact.Text));
    }
}
