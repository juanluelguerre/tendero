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

    // ---------- the terms: what it charges, said before there is a cart ----------

    /// <summary>A country a term names, or one this provider serves when the term is for everywhere.</summary>
    private string CountryOf(ShippingTerm term) => term.Countries?[0] ?? ServedCountry;

    [Fact]
    public void It_has_terms_a_shopper_can_read_in_either_language()
    {
        Assert.NotEmpty(Provider.Terms);
        Assert.All(Provider.Terms, term =>
        {
            Assert.False(string.IsNullOrWhiteSpace(term.Label.In("es")));
            Assert.False(string.IsNullOrWhiteSpace(term.Label.In("en")));
            Assert.False(term.Amount.IsNegative);
            Assert.True(term.EstimatedDays is null or > 0);
            Assert.True(term.Countries is null or { Count: > 0 });
        });
    }

    /// <summary>
    /// **The terms promise what a quote charges, and nothing else.** They are
    /// what the shop tells somebody who has not reached checkout yet, and a
    /// term that says 4.95 over a quote that charges 5.95 is the shop quoting
    /// one price and charging another, a page earlier.
    /// </summary>
    [Fact]
    public async Task Every_term_is_what_a_quote_charges_where_it_applies()
    {
        foreach (var term in Provider.Terms)
        {
            var quoted = (await Provider.QuoteAsync(Request(country: CountryOf(term), subtotal: 0.01m), Ct))
                .Single(option => option.Code == term.Code);

            Assert.Equal((term.Amount, term.EstimatedDays), (quoted.Amount, quoted.EstimatedDays));
        }
    }

    /// <summary>"Free from 50" in the terms is free at 50 in the quote, and not a cent before.</summary>
    [Fact]
    public async Task A_term_free_from_an_amount_is_quoted_free_from_exactly_that_amount()
    {
        foreach (var term in Provider.Terms.Where(term => term.FreeFrom is not null))
        {
            var from = term.FreeFrom!.Value.Amount;
            async Task<decimal> Charged(decimal subtotal) =>
                (await Provider.QuoteAsync(Request(country: CountryOf(term), subtotal: subtotal), Ct))
                .Single(option => option.Code == term.Code).Amount.Amount;

            Assert.Equal(0m, await Charged(from));
            Assert.Equal(term.Amount.Amount, await Charged(from - 0.01m));
        }
    }

    /// <summary>Nothing is quoted that the terms did not say: an option a shopper meets at checkout for the first time.</summary>
    [Fact]
    public async Task Every_option_quoted_is_one_of_the_terms()
    {
        var said = Provider.Terms.Select(term => term.Code).ToHashSet(StringComparer.Ordinal);

        Assert.All(await Provider.QuoteAsync(Request(), Ct), option => Assert.Contains(option.Code, said));
    }

    /// <summary>
    /// Where the terms say the shop delivers is where a quote has an option:
    /// a country no term names gets none, unless a term is for everywhere.
    /// </summary>
    [Fact]
    public async Task A_country_no_term_reaches_is_quoted_nothing()
    {
        var everywhere = Provider.Terms.Any(term => term.Countries is null);
        var named = Provider.Terms.SelectMany(term => term.Countries ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("AU", named);

        var quoted = await Provider.QuoteAsync(Request(country: "AU"), Ct);

        Assert.Equal(everywhere, quoted.Count > 0);
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

    /// <summary>Its terms are for everywhere, and the threshold is the standard one's alone.</summary>
    [Fact]
    public void Its_terms_name_no_country_and_only_standard_delivery_is_ever_free()
    {
        Assert.All(Provider.Terms, term => Assert.Null(term.Countries));
        Assert.Equal(
            [("standard", (Money?)new Money(50m, "EUR")), ("express", null)],
            Provider.Terms.Select(term => (term.Code, term.FreeFrom)));
    }

    /// <summary>A threshold of zero is no threshold: the terms promise nothing free, as the quote gives nothing free.</summary>
    [Fact]
    public void With_the_threshold_disabled_the_terms_promise_nothing_free()
    {
        var provider = new FlatRateShipping(Options.Create(new FlatRateShippingOptions { FreeAbove = 0m }));

        Assert.All(provider.Terms, term => Assert.Null(term.FreeFrom));
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

        // And the terms say it as a shopper is told it: the code, not what somebody typed.
        Assert.Equal(["ES"], Assert.Single(provider.Terms).Countries);
    }

    /// <summary>A term a zone, cheapest first, with its countries in order; a zone that reaches nowhere is not a way of sending.</summary>
    [Fact]
    public void Its_terms_are_its_zones_cheapest_first_with_the_countries_each_reaches()
    {
        var provider = new ZoneRateShipping(Options.Create(new ZoneRateShippingOptions
        {
            Zones = new(StringComparer.OrdinalIgnoreCase)
            {
                ["europe"] = new() { Countries = ["FR", "DE", "FR"], Standard = 8.95m, EstimatedDays = 5 },
                ["nowhere"] = new() { Countries = [], Standard = 1m },
                ["peninsula"] = new() { Countries = ["PT", "ES"], Standard = 3.95m, EstimatedDays = 2 },
            }
        }));

        Assert.Equal(
            [("zone-peninsula", 3.95m, "ES,PT"), ("zone-europe", 8.95m, "DE,FR")],
            provider.Terms.Select(term => (term.Code, term.Amount.Amount, string.Join(',', term.Countries!))));
    }
}
