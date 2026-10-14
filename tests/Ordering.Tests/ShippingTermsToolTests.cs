using System.Text.Json;
using ElGuerre.Tendero.Ordering.Adapters;
using ElGuerre.Tendero.Ordering.Features.ShippingTerms;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// What the storefront's assistant may read of where the shop delivers and what
/// delivery costs (after phase 13a). The shop already knew: checkout quotes it.
/// What it could not do was say it without a cart and an address, so a shopper
/// who asked was told nothing was known — or was read their orders, the nearest
/// tool there was.
/// </summary>
public sealed class ShippingTermsToolTests
{
    /// <summary>The dispatcher over the terms' own handler, with the provider checkout is configured to ask.</summary>
    private sealed class Shop(IShippingRateProvider provider) : IQueryDispatcher, IShippingRateProviderRegistry
    {
        public IReadOnlyCollection<string> Keys => [provider.Key];

        public IShippingRateProvider Get(string key) =>
            key == provider.Key ? provider : throw new InvalidOperationException($"asked for '{key}'");

        public async Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
            (TResult)(object)await new ShippingTermsHandler(this, Options.Create(new CheckoutOptions { ShippingProvider = provider.Key }))
                .HandleAsync((ShippingTermsQuery)(object)query, cancellationToken);
    }

    private static readonly IShippingRateProvider FlatRate = new FlatRateShipping(Options.Create(new FlatRateShippingOptions()));

    private static readonly IShippingRateProvider ZoneRate = new ZoneRateShipping(Options.Create(new ZoneRateShippingOptions()));

    private static Task<ReadAnswer> Read(IShippingRateProvider provider, string arguments) =>
        new ShippingTermsTool(new Shop(provider)).ReadAsync(JsonDocument.Parse(arguments).RootElement.Clone(), TestContext.Current.CancellationToken);

    private static IEnumerable<string> Said(ReadAnswer answer) => answer.Facts!.Select(fact => fact.Text);

    [Fact]
    public void It_is_a_shoppers_read()
    {
        var tool = new ShippingTermsTool(new Shop(FlatRate));

        Assert.Equal((ToolAudience.Shopper, "get_shipping_terms"), (tool.Audience, tool.Name));
    }

    /// <summary>One price wherever it goes: where it delivers, what each way costs and takes, and from what order it is free.</summary>
    [Fact]
    public async Task A_shop_that_charges_one_price_says_so_and_what_each_way_costs()
    {
        var answer = await Read(FlatRate, """{"culture":"es"}""");

        Assert.Equal(
        [
            "La tienda hace envíos a cualquier país, al mismo precio.",
            "Envío estándar cuesta 4,95 € y tarda unos 3 días.",
            "Envío estándar es gratis en pedidos desde 50,00 €.",
            "Envío exprés cuesta 9,95 € y tarda 1 día.",
        ], Said(answer));
        Assert.All(answer.Facts!, fact => Assert.Equal((Fact.TheShop, FactSource.Policy), (fact.Subject, fact.Source)));
        Assert.Equal((4, (int?)null), (answer.Rows, answer.Total));
        Assert.Empty(answer.Subjects!);
    }

    [Fact]
    public async Task It_says_it_in_english_when_asked_to()
    {
        var answer = await Read(FlatRate, """{"culture":"en"}""");

        Assert.Equal(
        [
            "The shop delivers to every country, at the same price.",
            "Standard delivery costs €4.95 and takes about 3 days.",
            "Standard delivery is free on orders from €50.00.",
            "Express delivery costs €9.95 and takes 1 day.",
        ], Said(answer));
    }

    /// <summary>A country named changes nothing for a shop that charges the same everywhere — and it is still said that it delivers there.</summary>
    [Fact]
    public async Task A_country_asked_of_a_shop_that_delivers_everywhere_gets_the_same_terms()
    {
        var answer = await Read(FlatRate, """{"culture":"es","country":"PT"}""");

        Assert.Equal("La tienda hace envíos a cualquier país, al mismo precio.", Said(answer).First());
        Assert.Equal(4, answer.Rows);
        Assert.True(((ShippingTermsForShopper)answer.Data).Delivers);
    }

    /// <summary>With no country named, a shop that charges by zone says every country it reaches and each zone's price.</summary>
    [Fact]
    public async Task A_shop_that_charges_by_zone_says_where_it_reaches_and_what_each_zone_costs()
    {
        var answer = await Read(ZoneRate, """{"culture":"en"}""");

        Assert.Equal(
        [
            "The shop delivers to these countries: BE, DE, ES, FR, IT, NL, PT.",
            "Delivery to the Peninsula (ES, PT) costs €3.95 and takes about 2 days.",
            "Delivery to Europe (BE, DE, FR, IT, NL) costs €8.95 and takes about 5 days.",
        ], Said(answer));
        Assert.Null(((ShippingTermsForShopper)answer.Data).Delivers);
    }

    /// <summary>
    /// **Whether the shop delivers to a country is code's to say**, not a model's
    /// to read off a list: the country comes in as its code, and what comes back
    /// says yes and only that zone's price.
    /// </summary>
    [Fact]
    public async Task A_country_the_shop_reaches_gets_a_yes_and_its_own_zone()
    {
        var answer = await Read(ZoneRate, """{"culture":"es","country":"pt"}""");

        Assert.Equal(
        [
            "La tienda hace envíos a PT.",
            "Envío a la Península (ES, PT) cuesta 3,95 € y tarda unos 2 días.",
        ], Said(answer));
        Assert.True(((ShippingTermsForShopper)answer.Data).Delivers);
    }

    /// <summary>A country nobody ships to is a sentence, as it is at checkout — and where the shop does reach is said beside it.</summary>
    [Theory]
    [InlineData("es", "La tienda todavía no hace envíos a AU.", "La tienda hace envíos a estos países: BE, DE, ES, FR, IT, NL, PT.")]
    [InlineData("en", "The shop does not deliver to AU yet.", "The shop delivers to these countries: BE, DE, ES, FR, IT, NL, PT.")]
    public async Task A_country_the_shop_does_not_reach_gets_a_no_and_where_it_does(string culture, string no, string where)
    {
        var answer = await Read(ZoneRate, $$"""{"culture":"{{culture}}","country":"AU"}""");

        Assert.Equal([no, where], Said(answer));
        Assert.False(((ShippingTermsForShopper)answer.Data).Delivers);
    }

    /// <summary>A country written as its name is the model's mistake, and it is told so in words it can correct.</summary>
    [Fact]
    public async Task A_country_that_is_not_a_code_is_an_error_the_model_can_read()
    {
        var refused = await Assert.ThrowsAsync<ArgumentException>(() => Read(ZoneRate, """{"country":"Portugal"}"""));

        Assert.Contains("ISO 3166-1 alpha-2", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The description is what makes a model call it for a shipping question — and my_orders's sends it here.</summary>
    [Fact]
    public void Its_description_names_what_a_shopper_asks_about_shipping()
    {
        var description = new ShippingTermsTool(new Shop(FlatRate)).Description;

        Assert.Contains("delivers", description, StringComparison.Ordinal);
        Assert.Contains("costs", description, StringComparison.Ordinal);
        Assert.Contains("how long", description, StringComparison.Ordinal);
    }
}
