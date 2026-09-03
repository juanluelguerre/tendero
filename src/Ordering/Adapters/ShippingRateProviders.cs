using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Ordering.Adapters;

/// <summary>
/// What the flat-rate carrier charges. Configuration rather than constants,
/// because "free over 50 €" is the sort of number a shopkeeper changes without
/// a deployment — and because a threshold hidden in code is a threshold nobody
/// can find when it is wrong.
/// </summary>
public sealed class FlatRateShippingOptions
{
    public const string SectionName = "Shipping:FlatRate";

    public decimal Standard { get; set; } = 4.95m;

    public decimal Express { get; set; } = 9.95m;

    /// <summary>Above this, standard delivery is free. Zero disables it.</summary>
    public decimal FreeAbove { get; set; } = 50m;

    public string Currency { get; set; } = "EUR";
}

/// <summary>
/// One price, everywhere, free over a threshold.
///
/// **It ignores the destination completely**, and that is what makes it the
/// useful half of the pair: the contract suite asserts things every provider
/// must honour, and a provider that reads none of the request is the one most
/// likely to break them by accident.
/// </summary>
public sealed class FlatRateShipping(IOptions<FlatRateShippingOptions> options) : IShippingRateProvider
{
    public const string Key = "flat-rate";

    string IShippingRateProvider.Key => Key;

    public Task<IReadOnlyList<ShippingOption>> QuoteAsync(
        ShippingQuoteRequest request, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var currency = settings.Currency;

        var free = settings.FreeAbove > 0m && request.Subtotal.Amount >= settings.FreeAbove;

        IReadOnlyList<ShippingOption> rates =
        [
            new(
                "standard",
                new LocalizedText(new Dictionary<string, string>
                {
                    ["es"] = free ? "Envío estándar gratis" : "Envío estándar",
                    ["en"] = free ? "Free standard delivery" : "Standard delivery"
                }),
                new Money(free ? 0m : settings.Standard, currency),
                EstimatedDays: 3),

            new(
                "express",
                new LocalizedText(new Dictionary<string, string>
                {
                    ["es"] = "Envío exprés",
                    ["en"] = "Express delivery"
                }),
                new Money(settings.Express, currency),
                EstimatedDays: 1)
        ];

        return Task.FromResult(rates);
    }
}

/// <summary>A band of countries and what it costs to reach them.</summary>
public sealed class ShippingZone
{
    /// <summary>ISO 3166-1 alpha-2, uppercase. Normalised on read, because a
    /// configuration file written by a human will contain "es".</summary>
    public List<string> Countries { get; set; } = [];

    public decimal Standard { get; set; }

    public int EstimatedDays { get; set; } = 5;
}

public sealed class ZoneRateShippingOptions
{
    public const string SectionName = "Shipping:ZoneRate";

    public string Currency { get; set; } = "EUR";

    /// <summary>
    /// Keyed by zone name, because the name is what the shopper reads and what a
    /// report groups by. Two zones at laboratory scale; a third is a
    /// configuration entry, not a code change.
    /// </summary>
    public Dictionary<string, ShippingZone> Zones { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["peninsula"] = new() { Countries = ["ES", "PT"], Standard = 3.95m, EstimatedDays = 2 },
        ["europe"] = new() { Countries = ["FR", "DE", "IT", "NL", "BE"], Standard = 8.95m, EstimatedDays = 5 }
    };
}

/// <summary>
/// A price per destination zone, and **nothing at all for a country nobody
/// ships to**.
///
/// The empty answer is the interesting one. A country outside the table is a
/// business fact, not an error, so it comes back as no options and checkout says
/// so in a sentence — rather than throwing and showing a stack trace where a
/// shop should show "we do not deliver to Australia yet".
/// </summary>
public sealed class ZoneRateShipping(IOptions<ZoneRateShippingOptions> options) : IShippingRateProvider
{
    public const string Key = "zone-rate";

    string IShippingRateProvider.Key => Key;

    public Task<IReadOnlyList<ShippingOption>> QuoteAsync(
        ShippingQuoteRequest request, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var country = Address.NormaliseCountry(request.Destination.CountryCode);

        var zone = settings.Zones.FirstOrDefault(entry => entry.Value.Countries
            .Any(candidate => string.Equals(candidate.Trim(), country, StringComparison.OrdinalIgnoreCase)));

        IReadOnlyList<ShippingOption> rates = zone.Value is null
            ? []
            :
            [
                new(
                    $"zone-{zone.Key.ToLowerInvariant()}",
                    new LocalizedText(new Dictionary<string, string>
                    {
                        ["es"] = $"Envío a {ZoneName(zone.Key, "es")}",
                        ["en"] = $"Delivery to {ZoneName(zone.Key, "en")}"
                    }),
                    new Money(zone.Value.Standard, settings.Currency),
                    zone.Value.EstimatedDays)
            ];

        return Task.FromResult(rates);
    }

    /// <summary>
    /// Zone names are configuration keys, and configuration keys are not
    /// user-facing text. The two the shop ships with get a real label in both
    /// languages; anything added later falls back to its key rather than being
    /// silently untranslated, which is at least visible.
    /// </summary>
    private static string ZoneName(string zone, string culture) => (zone.ToLowerInvariant(), culture) switch
    {
        ("peninsula", "es") => "la Península",
        ("peninsula", _) => "the Peninsula",
        ("europe", "es") => "Europa",
        ("europe", _) => "Europe",
        _ => zone
    };
}

internal sealed class KeyedShippingRateProviderRegistry(IServiceProvider services) : IShippingRateProviderRegistry
{
    public IReadOnlyCollection<string> Keys =>
        [.. services.GetKeyedServices<IShippingRateProvider>(KeyedService.AnyKey)
            .Select(provider => provider.Key)
            .Order(StringComparer.Ordinal)];

    public IShippingRateProvider Get(string key) =>
        services.GetKeyedService<IShippingRateProvider>(key)
        ?? throw new UnknownShippingProviderException(key, Keys);
}

public sealed class UnknownShippingProviderException(string key, IReadOnlyCollection<string> known)
    : InvalidOperationException($"There is no shipping provider '{key}'. Known: {string.Join(", ", known)}.")
{
    public string Key { get; } = key;
}
