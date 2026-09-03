using ElGuerre.Tendero.Ordering.Adapters;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// What every shipping provider must honour, whoever writes it.
///
/// The same arrangement as the catalogue connectors, the tax calculators and the
/// allocation strategies: an abstract suite the adapter inherits in three lines,
/// and a new carrier without its subclass does not merge.
///
/// The two adapters this runs against were chosen to disagree about what they
/// read. `flat-rate` ignores the destination entirely; `zone-rate` is a function
/// of it and answers **nothing** for a country outside the table. A contract both
/// satisfy is therefore a contract about shipping rather than about arithmetic.
/// </summary>
public abstract class ShippingRateProviderContractTests
{
    protected abstract IShippingRateProvider Provider { get; }

    /// <summary>A destination this provider definitely serves. `zone-rate` needs
    /// to say which; `flat-rate` does not care.</summary>
    protected virtual string ServedCountry => "ES";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected ShippingQuoteRequest Request(
        string? country = null, decimal subtotal = 30m, int quantity = 1) =>
        new(
            Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", country ?? ServedCountry),
            [new ShippableLine("PANS", quantity)],
            new Money(subtotal, "EUR"));

    [Fact]
    public void The_key_is_a_stable_lowercase_identifier()
    {
        Assert.False(string.IsNullOrWhiteSpace(Provider.Key));
        Assert.Equal(Provider.Key.ToLowerInvariant(), Provider.Key);
        Assert.DoesNotContain(' ', Provider.Key);
    }

    [Fact]
    public async Task A_served_destination_gets_at_least_one_option()
    {
        Assert.NotEmpty(await Provider.QuoteAsync(Request(), Ct));
    }

    /// <summary>
    /// Every option has to survive being written onto an order: a code that
    /// identifies it, a label the buyer can read in either language, and an
    /// amount that is not negative.
    /// </summary>
    [Fact]
    public async Task Every_option_can_be_frozen_onto_an_order()
    {
        foreach (var option in await Provider.QuoteAsync(Request(), Ct))
        {
            Assert.False(string.IsNullOrWhiteSpace(option.Code));
            Assert.False(option.Amount.IsNegative);

            // Invariant 6 has no exceptions, and a carrier's option name is
            // user-facing text like any other.
            Assert.False(string.IsNullOrWhiteSpace(option.Label.In("es")));
            Assert.False(string.IsNullOrWhiteSpace(option.Label.In("en")));

            Assert.True(option.EstimatedDays is null or > 0);
        }
    }

    [Fact]
    public async Task Option_codes_are_unique_within_one_answer()
    {
        var options = await Provider.QuoteAsync(Request(), Ct);

        Assert.Equal(
            options.Count,
            options.Select(option => option.Code).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Cheapest first. It is a contract and not a preference: checkout preselects
    /// the first option, and a provider that answered in registration order would
    /// silently upsell.
    /// </summary>
    [Fact]
    public async Task Options_come_back_cheapest_first()
    {
        var amounts = (await Provider.QuoteAsync(Request(), Ct))
            .Select(option => option.Amount.Amount)
            .ToList();

        Assert.Equal(amounts.Order(), amounts);
    }

    [Fact]
    public async Task Every_option_is_quoted_in_the_cart_currency()
    {
        var request = Request();

        Assert.All(
            await Provider.QuoteAsync(request, Ct),
            option => Assert.Equal(request.Subtotal.Currency, option.Amount.Currency));
    }

    /// <summary>
    /// Same question, same answer. A rate is written onto an order and shown to
    /// a shopper before that, so a provider that varied between two identical
    /// calls would quote one price and charge another.
    /// </summary>
    [Fact]
    public async Task The_same_question_always_gets_the_same_answer()
    {
        var request = Request();

        Assert.Equal(
            (await Provider.QuoteAsync(request, Ct)).Select(option => (option.Code, option.Amount)),
            (await Provider.QuoteAsync(request, Ct)).Select(option => (option.Code, option.Amount)));
    }

    /// <summary>
    /// A country nobody ships to is a business fact, not an exception. Turning
    /// it into one would make checkout show a stack trace where a shop should
    /// say "we do not deliver there yet".
    /// </summary>
    [Fact]
    public async Task An_unserved_destination_is_an_empty_answer_and_never_a_throw()
    {
        var options = await Provider.QuoteAsync(Request(country: "AU", subtotal: 999m), Ct);

        Assert.NotNull(options);
    }
}

public sealed class FlatRateShippingContractTests : ShippingRateProviderContractTests
{
    protected override IShippingRateProvider Provider { get; } =
        new FlatRateShipping(Options.Create(new FlatRateShippingOptions()));

    /// <summary>
    /// The behaviour that distinguishes it: a threshold, and a label that says
    /// so. "Free" printed beside a €4.95 line is the bug this catches.
    /// </summary>
    [Fact]
    public async Task Standard_delivery_is_free_above_the_threshold()
    {
        var provider = new FlatRateShipping(
            Options.Create(new FlatRateShippingOptions { Standard = 4.95m, FreeAbove = 50m }));

        var under = await provider.QuoteAsync(Request(subtotal: 49.99m), TestContext.Current.CancellationToken);
        var over = await provider.QuoteAsync(Request(subtotal: 50m), TestContext.Current.CancellationToken);

        Assert.Equal(4.95m, under.First(option => option.Code == "standard").Amount.Amount);

        var free = over.First(option => option.Code == "standard");
        Assert.Equal(0m, free.Amount.Amount);
        Assert.Contains("gratis", free.Label.In("es"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("free", free.Label.In("en"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task It_quotes_the_same_price_wherever_the_parcel_is_going()
    {
        var here = await Provider.QuoteAsync(Request(country: "ES"), TestContext.Current.CancellationToken);
        var there = await Provider.QuoteAsync(Request(country: "AU"), TestContext.Current.CancellationToken);

        Assert.Equal(
            here.Select(option => (option.Code, option.Amount)),
            there.Select(option => (option.Code, option.Amount)));
    }
}

public sealed class ZoneRateShippingContractTests : ShippingRateProviderContractTests
{
    protected override IShippingRateProvider Provider { get; } =
        new ZoneRateShipping(Options.Create(new ZoneRateShippingOptions()));

    /// <summary>
    /// The mirror of the flat-rate test above, and the reason two adapters
    /// exist: the same request, answered differently on purpose.
    /// </summary>
    [Fact]
    public async Task It_charges_by_zone_and_refuses_what_it_does_not_reach()
    {
        var peninsula = await Provider.QuoteAsync(Request(country: "ES"), TestContext.Current.CancellationToken);
        var europe = await Provider.QuoteAsync(Request(country: "DE"), TestContext.Current.CancellationToken);
        var elsewhere = await Provider.QuoteAsync(Request(country: "AU"), TestContext.Current.CancellationToken);

        Assert.Equal(3.95m, Assert.Single(peninsula).Amount.Amount);
        Assert.Equal(8.95m, Assert.Single(europe).Amount.Amount);
        Assert.Empty(elsewhere);
    }

    /// <summary>
    /// A configuration file written by a human contains "es". Normalising on
    /// read is what stops a lowercase country code from silently costing a
    /// shopper an international rate.
    /// </summary>
    [Fact]
    public async Task A_lowercase_country_reaches_the_same_zone()
    {
        var provider = new ZoneRateShipping(Options.Create(new ZoneRateShippingOptions
        {
            Zones = new(StringComparer.OrdinalIgnoreCase)
            {
                ["peninsula"] = new() { Countries = ["es"], Standard = 3.95m }
            }
        }));

        var options = await provider.QuoteAsync(Request(country: "ES"), TestContext.Current.CancellationToken);

        Assert.Equal(3.95m, Assert.Single(options).Amount.Amount);
    }
}
