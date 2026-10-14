using System.ComponentModel;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;
using NodaTime;

namespace ElGuerre.Tendero.Pricing.Features.ListCampaigns;

public sealed record CampaignsArguments(
    [property: Description("The language to answer in: es or en.")] string? Culture = null);

/// <summary>What the shop runs, as sentences: one a campaign.</summary>
public sealed record CampaignsForShopper(IReadOnlyList<string> Says);

/// <summary>
/// <c>list_campaigns</c> for the storefront's assistant (phase 13c): the shop's
/// campaigns running now or about to start, as the home's card is drawn from
/// them (the same query), said as sentences Tino can cite. Asked "¿alguna
/// oferta para hoy?", Tino answered that it had no such data while a campaign
/// was running: none of its reads said what the shop runs.
///
/// A campaign's last day is the day before its exclusive end, counted in the
/// shop's zone (ADR 0046). Each promotion says what it takes off and what it
/// needs, from the promotion itself: "5 € en menaje" is 5 € off the order once it
/// has an item in the promotion and comes to 40 € — said by its name alone, Tino could not
/// tell a shopper what "esos 5 €" needed. A promotion behind a coupon says the
/// code to type. What one takes off a particular basket is the quote's to say,
/// not this.
/// </summary>
public sealed class ListCampaignsTool(IQueryDispatcher queries, IPromotionReader promotions, IOptions<ShopOptions> shop)
    : ReadTool<CampaignsArguments, CampaignsForShopper>(queries)
{
    public override ToolAudience Audience => ToolAudience.Shopper;

    public override string Name => "list_campaigns";

    public override string Description =>
        "The shop's offers: the campaigns running now or starting soon, each with what its promotions take off, what they need and until when, and the code to type when one needs it. Read it when the shopper asks about offers, sales or discounts in general.";

    protected override async Task<(CampaignsForShopper Data, int Rows, int? Total)> ReadAsync(
        CampaignsArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var culture = CultureNegotiation.Resolve(arguments.Culture, default);
        var result = await queries.SendAsync(new ListCampaignsQuery(culture), cancellationToken);
        var terms = (await promotions.AllAsync(cancellationToken)).ToDictionary(promotion => promotion.Code, StringComparer.Ordinal);
        var words = new CampaignWords(culture, DateTimeZoneProviders.Tzdb[shop.Value.TimeZone], terms);

        IReadOnlyList<string> says = result.Items.Count == 0 ? [words.Nothing] : [.. result.Items.Select(words.Of)];
        return (new CampaignsForShopper(says), result.Items.Count, result.Items.Count);
    }

    protected override IReadOnlyList<Fact> Facts(CampaignsForShopper data) =>
        [.. data.Says.Select(sentence => new Fact(Fact.TheShop, FactSource.Campaign, sentence))];
}

internal sealed class CampaignWords(string culture, DateTimeZone zone, IReadOnlyDictionary<string, Promotion> promotions)
{
    private bool English => culture == "en";

    public string Nothing => English
        ? "The shop has no campaign running or about to start."
        : "La tienda no tiene ninguna campaña en marcha ni a punto de empezar.";

    public string Of(CampaignView campaign)
    {
        var name = English ? $"“{campaign.Name}”" : $"«{campaign.Name}»";
        var when = campaign.Phase == CampaignPhase.Running
            ? English
                ? $"{name} is running until {Day(campaign.EndsAt.AddTicks(-1))}"
                : $"{name} está en marcha hasta el {Day(campaign.EndsAt.AddTicks(-1))}"
            : English
                ? $"{name} starts on {Day(campaign.StartsAt)}"
                : $"{name} empieza el {Day(campaign.StartsAt)}";

        return campaign.Holds.Count == 0 ? $"{when}." : $"{when}: {string.Join("; ", campaign.Holds.Select(Hold))}.";
    }

    private string Hold(CampaignHold hold)
    {
        var name = English ? $"“{hold.Name}”" : $"«{hold.Name}»";
        var said = promotions.GetValueOrDefault(hold.Code) is { } promotion ? $"{name}, {DealText.OfThePromotion(promotion.Terms, culture)}" : name;
        return hold.CouponCode is { } code
            ? English ? $"{said}, with the code {code}" : $"{said}, con el código {code}"
            : said;
    }

    private string Day(DateTimeOffset moment)
    {
        var day = Instant.FromDateTimeOffset(moment).InZone(zone).Date;
        return DayText.In(day.Year, day.Month, day.Day, culture);
    }
}
