using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Ordering.Features.ShippingTerms;

/// <summary>
/// Where the shop delivers and what delivery costs, asked before there is a cart
/// (after phase 13a): the terms of the provider checkout is configured to quote
/// with, and — when a country is named — whether the shop reaches it.
///
/// **Whether it does is decided here, not read off a list by a model.** The
/// country comes as its code and the answer comes back a yes or a no, with only
/// the terms that apply there.
/// </summary>
/// <param name="Country">ISO 3166-1 alpha-2, in any case; null when the question names no country.</param>
public sealed record ShippingTermsQuery(string? Country) : IQuery<ShippingTermsResult>;

/// <param name="Country">The country asked about, normalised; null when none was.</param>
/// <param name="Delivers">Whether the shop delivers there; null when no country was asked.</param>
/// <param name="Terms">The ways of sending: those that reach the country when one was asked and is reached, all of them otherwise.</param>
/// <param name="Reaches">Every country the shop delivers to, in order; null when it delivers wherever the parcel goes.</param>
public sealed record ShippingTermsResult(
    string? Country, bool? Delivers, IReadOnlyList<ShippingTerm> Terms, IReadOnlyList<string>? Reaches);

public sealed class ShippingTermsHandler(IShippingRateProviderRegistry shipping, IOptions<CheckoutOptions> options)
    : IQueryHandler<ShippingTermsQuery, ShippingTermsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public Task<ShippingTermsResult> HandleAsync(ShippingTermsQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.shipping_terms");

        // The provider placing an order asks, so what is said here is what is charged there.
        var terms = shipping.Get(options.Value.ShippingProvider).Terms;
        activity?.SetTag("shipping.provider", options.Value.ShippingProvider);

        // One term for everywhere and the shop delivers everywhere, whatever the others name.
        IReadOnlyList<string>? reaches = terms.Any(term => term.Countries is null)
            ? null
            : [.. terms.SelectMany(term => term.Countries!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        if (string.IsNullOrWhiteSpace(query.Country))
            return Task.FromResult(new ShippingTermsResult(null, null, terms, reaches));

        // Anything that is not two letters is refused here, in words a model can read.
        var country = Address.NormaliseCountry(query.Country);
        activity?.SetTag("shipping.country", country);

        IReadOnlyList<ShippingTerm> there = [.. terms.Where(term => term.Countries?.Contains(country) ?? true)];
        activity?.SetTag("shipping.delivers", there.Count > 0);

        return Task.FromResult(there.Count > 0
            ? new ShippingTermsResult(country, true, there, reaches)
            : new ShippingTermsResult(country, false, [], reaches));
    }
}

// ---------- the read ----------

public sealed record ShippingTermsArguments(
    [property: Description("The language to answer in: es or en.")] string? Culture = null,
    [property: Description("The country the shopper asks about, as its two-letter ISO code: Portugal is PT, Germany is DE. Leave it out when they name no country.")] string? Country = null);

/// <param name="Delivers">Whether the shop delivers to the country asked about; null when none was.</param>
/// <param name="Says">The terms as sentences, in the language asked for.</param>
public sealed record ShippingTermsForShopper(bool? Delivers, IReadOnlyList<string> Says);

/// <summary>
/// <c>get_shipping_terms</c> (after phase 13a): where the shop delivers, what each
/// way of sending costs, how long it takes and from what order it is free — as
/// sentences an answer may cite, in es and en (invariant 6).
///
/// Until this there was no read that answered a shipping question, and the
/// shopper's assistant took the nearest one there was: asked *¿hacéis envíos a
/// Portugal?*, it read the shopper's orders. The shop knew the answer the whole
/// time — checkout quotes it.
/// </summary>
public sealed class ShippingTermsTool(IQueryDispatcher queries) : ReadTool<ShippingTermsArguments, ShippingTermsForShopper>(queries)
{
    public override ToolAudience Audience => ToolAudience.Shopper;

    public override string Name => "get_shipping_terms";

    public override string Description =>
        "Shipping in general: where the shop delivers, what delivery costs, how long it takes and from what order it is free. Pass the country when the shopper names one, as its two-letter code. It says nothing about an order already placed.";

    protected override async Task<(ShippingTermsForShopper Data, int Rows, int? Total)> ReadAsync(
        ShippingTermsArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var result = await queries.SendAsync(new ShippingTermsQuery(arguments.Country), cancellationToken);
        var says = new ShippingWords(CultureNegotiation.Resolve(arguments.Culture, default)).Of(result);
        return (new ShippingTermsForShopper(result.Delivers, says), says.Count, null);
    }

    protected override IReadOnlyList<Fact> Facts(ShippingTermsForShopper data) =>
        [.. data.Says.Select(sentence => new Fact(Fact.TheShop, FactSource.Policy, sentence))];
}

/// <summary>
/// The shop's shipping terms in the shopper's words, es and en (invariant 6).
/// A country is written as its code: the host runs with invariant globalization,
/// where a country's name in a language is not there to ask, and a table of names
/// here would be a second list of the countries the shop reaches.
/// </summary>
internal sealed class ShippingWords(string culture)
{
    private bool English => culture == "en";

    public IReadOnlyList<string> Of(ShippingTermsResult result)
    {
        List<string> says = [];

        // Where, first: it is what "do you deliver to …?" asks, and what the prices are read under.
        switch (result)
        {
            case { Reaches: null }:
                says.Add(English
                    ? "The shop delivers to every country, at the same price."
                    : "La tienda hace envíos a cualquier país, al mismo precio.");
                break;
            case { Delivers: true, Country: { } country }:
                says.Add(English ? $"The shop delivers to {country}." : $"La tienda hace envíos a {country}.");
                break;
            case { Delivers: false, Country: { } country, Reaches: var reaches }:
                says.Add(English ? $"The shop does not deliver to {country} yet." : $"La tienda todavía no hace envíos a {country}.");
                says.Add(Where(reaches));
                break;
            case { Reaches: var reaches }:
                says.Add(Where(reaches));
                break;
        }

        foreach (var term in result.Terms)
        {
            says.Add(Costs(term));
            if (term.FreeFrom is { } from)
            {
                says.Add(English
                    ? $"{term.Label.In(culture)} is free on orders from {Amount(from)}."
                    : $"{term.Label.In(culture)} es gratis en pedidos desde {Amount(from)}.");
            }
        }

        return says;
    }

    private string Where(IReadOnlyList<string> reaches) => English
        ? $"The shop delivers to these countries: {string.Join(", ", reaches)}."
        : $"La tienda hace envíos a estos países: {string.Join(", ", reaches)}.";

    private string Costs(ShippingTerm term)
    {
        var what = term.Label.In(culture) + (term.Countries is { } countries ? $" ({string.Join(", ", countries)})" : "");

        return (term.EstimatedDays, English) switch
        {
            (null, true) => $"{what} costs {Amount(term.Amount)}.",
            (null, false) => $"{what} cuesta {Amount(term.Amount)}.",
            (1, true) => $"{what} costs {Amount(term.Amount)} and takes 1 day.",
            (1, false) => $"{what} cuesta {Amount(term.Amount)} y tarda 1 día.",
            ({ } days, true) => $"{what} costs {Amount(term.Amount)} and takes about {days} days.",
            ({ } days, false) => $"{what} cuesta {Amount(term.Amount)} y tarda unos {days} días.",
        };
    }

    /// <summary>As an order's total is written for the same reader: two decimals and the currency's code.</summary>
    private static string Amount(Money money) =>
        string.Create(CultureInfo.InvariantCulture, $"{money.Amount:0.00} {money.Currency}");
}
