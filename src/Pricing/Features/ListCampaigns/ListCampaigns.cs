using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Pricing.Features.ListCampaigns;

/// <summary>
/// A campaign as a shopper, or an agent, may see it (R-8): what it is called,
/// what it holds and when it runs. The backoffice's view is another slice and
/// another type, as the offers are beside the promotions: this one leaves out the
/// status, the dates it was edited, and every campaign not scheduled.
/// </summary>
/// <param name="Code">What the listing's <c>?campaign=</c> carries.</param>
/// <param name="Scope">The shop's home shows a <c>Shop</c> campaign; a <c>Category</c> one is that category's banner.</param>
/// <param name="CategoryCode">The category a <c>Category</c> campaign belongs to; null for the shop.</param>
/// <param name="Phase">Upcoming or Running — never Over, which the list does not answer.</param>
/// <param name="StartsAt">Inclusive. The clock is the browser's, computed from the window, so a cached answer never counts down wrong.</param>
/// <param name="EndsAt">Exclusive.</param>
/// <param name="Holds">What it holds, in the grouping's order: every promotion anybody can have — no segment — and, for one behind a coupon, the code to type.</param>
/// <param name="ImageId">The key in the image store (<c>/api/images/{imageId}</c>); null when the campaign has no picture of its own.</param>
/// <param name="ImageAlt">The picture's alt text in the answer's culture; null with no picture.</param>
public sealed record CampaignView(
    string Code,
    string Name,
    string Line,
    CampaignKind Kind,
    CampaignScope Scope,
    string? CategoryCode,
    CampaignPhase Phase,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int Priority,
    IReadOnlyList<CampaignHold> Holds,
    string? ImageId,
    string? ImageAlt);

/// <summary>
/// One promotion a campaign holds, as the card says it, by its <paramref name="Code"/>
/// — what the shopper's assistant reads its terms by (phase 13c). <paramref name="CouponCode"/>
/// is null for a promotion that applies by itself, and the code to type for one
/// that needs it — data, not a sentence, so each language phrases it (the page:
/// "con el código BF2026"). A coupon promotion is shown but not counted in
/// <c>?campaign=</c>: a price that needs a code is not the price the listing
/// shows everybody.
/// </summary>
public sealed record CampaignHold(string Code, string Name, string? CouponCode);

/// <summary>The leading campaign first; empty when none is near — a home never shows an empty stage.</summary>
public sealed record ListCampaignsResult(IReadOnlyList<CampaignView> Items);

public sealed record ListCampaignsQuery(string Culture) : IQuery<ListCampaignsResult>;

public sealed class ListCampaignsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/pricing/campaigns?culture=es
        app.MapGet("/api/pricing/campaigns",
            async Task<Ok<ListCampaignsResult>> (
                   string? culture, HttpContext http,
                   IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(new ListCampaignsQuery(resolved), ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return TypedResults.Ok(result);
            })
            // A campaign is the shop's window display: public by definition, as the offers are.
            .AllowAnonymous()
            .WithTags("Pricing")
            .WithName("ListCampaigns");
    }
}

public sealed class ListCampaignsHandler(ICampaignReader campaigns, IPromotionReader promotions, TimeProvider clock)
    : IQueryHandler<ListCampaignsQuery, ListCampaignsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Pricing);

    public async Task<ListCampaignsResult> HandleAsync(ListCampaignsQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("pricing.list_campaigns");

        var now = clock.GetUtcNow();
        var near = CampaignLineup.Near(await campaigns.ScheduledNotOverAsync(now, cancellationToken), now);

        // Only when something is near: an empty list asks nothing of the promotions.
        var known = near.Count == 0
            ? []
            : (await promotions.AllAsync(cancellationToken)).ToDictionary(promotion => promotion.Code, StringComparer.OrdinalIgnoreCase);

        activity?.SetTag("pricing.campaign_count", near.Count);

        return new ListCampaignsResult([.. near.Select(campaign => View(campaign, known, now, query.Culture))]);
    }

    private static CampaignView View(
        Campaign campaign, IReadOnlyDictionary<string, Promotion> promotions, DateTimeOffset now, string culture) => new(
        campaign.Code.Value,
        campaign.Name.In(culture),
        campaign.Line.In(culture),
        campaign.Kind,
        campaign.Scope,
        campaign.CategoryCode,
        campaign.PhaseAt(now),
        campaign.StartsAt,
        campaign.EndsAt,
        campaign.Priority,
        Holds(campaign, promotions, culture),
        campaign.Image?.Id.Value,
        campaign.Image?.Alt.In(culture));

    /// <summary>
    /// What the card says it holds. A promotion for a segment is left out — it is
    /// somebody's, not the shop's — and one behind a coupon is said with its code,
    /// which is how a campaign like Black Friday announces its discount (the
    /// author's seed: <c>BLACKFRIDAY</c> needs <c>BF2026</c>). A code that no longer
    /// names a promotion — the files changed after the campaign was saved — is
    /// simply not said.
    /// </summary>
    private static CampaignHold[] Holds(Campaign campaign, IReadOnlyDictionary<string, Promotion> promotions, string culture) =>
    [
        .. campaign.PromotionCodes
            .Select(code => promotions.GetValueOrDefault(code))
            .OfType<Promotion>()
            .Where(promotion => promotion.Segment is null)
            .Select(promotion => new CampaignHold(promotion.Code, promotion.Name.In(culture), promotion.CouponCode)),
    ];
}
