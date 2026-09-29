using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Ordering.Features.WhatSells;

/// <summary>
/// What sells, as the shop's home says it in public (R-8): *Lo más vendido* and
/// *Tendencias*, from Ordering's own figures and no telemetry.
///
/// **Ranked, never with counts** (the spec). The units and the money are the
/// shop's commercial information and stay on the shopkeeper's dashboard; the
/// home answers which products, in which order, and nothing a competitor could
/// add up. A product is answered by its id: the storefront builds the cards
/// through search (<c>GET /api/search/cards?ids=</c>), which is the one place
/// that knows a product's name, price and stock today — Ordering only knows what
/// an order snapshotted, and a card drawn from a snapshot would show last month's
/// price.
/// </summary>
/// <param name="Rank">1 for the first, in the order the row draws them.</param>
/// <param name="ProductId">What the storefront asks search for the card with.</param>
public sealed record RankedProduct(int Rank, string ProductId);

/// <param name="Days">The window the ranking was read over, ending now.</param>
/// <param name="Items">Empty when nothing qualifies — the page then draws no row.</param>
public sealed record RankedProducts(int Days, IReadOnlyList<RankedProduct> Items);

public sealed record BestSellersQuery(int Days, int Limit) : IQuery<RankedProducts>;

public sealed record TrendingQuery(int Days, int Limit) : IQuery<RankedProducts>;

/// <summary>The window and the row's length, and what the endpoints default to.</summary>
public static class WhatSellsWindow
{
    /// <summary>The spec's week.</summary>
    public const int DefaultDays = 7;

    /// <summary>A quarter at most: past that, "what sells" is history rather than news.</summary>
    public const int MaxDays = 90;

    /// <summary>A row the home scrolls: ten, as the new arrivals are.</summary>
    public const int DefaultLimit = 10;

    /// <summary>Two rows' worth; the storefront asks a few more than it draws, since a withdrawn product has no card.</summary>
    public const int MaxLimit = 24;
}

public sealed class BestSellersValidator : AbstractValidator<BestSellersQuery>
{
    public BestSellersValidator()
    {
        RuleFor(query => query.Days).InclusiveBetween(1, WhatSellsWindow.MaxDays).OverridePropertyName("days");
        RuleFor(query => query.Limit).InclusiveBetween(1, WhatSellsWindow.MaxLimit).OverridePropertyName("limit");
    }
}

public sealed class TrendingValidator : AbstractValidator<TrendingQuery>
{
    public TrendingValidator()
    {
        RuleFor(query => query.Days).InclusiveBetween(1, WhatSellsWindow.MaxDays).OverridePropertyName("days");
        RuleFor(query => query.Limit).InclusiveBetween(1, WhatSellsWindow.MaxLimit).OverridePropertyName("limit");
    }
}

/// <summary>
/// The two rows' rules, as pure functions over the orders a window holds, so each
/// is checked by a test with no database and the handler only fetches.
/// </summary>
public static class WhatSellsFigures
{
    /// <summary>
    /// The fewest units a product must sell in the window to be a trend.
    ///
    /// Three, because below it a "rise" is noise: one sale against none is an
    /// infinite rise and says nothing, and two against none is one shopper
    /// buying a pair. Three units is at least two purchases or one deliberate
    /// bulk one — the smallest number at which "more people are buying this"
    /// can be true. At laboratory scale it means the row is often empty, and the
    /// spec says so rather than faking one (R-8, *Tendencias*).
    /// </summary>
    public const int TrendFloor = 3;

    /// <summary>The window's best sellers, by product, first <paramref name="limit"/>.</summary>
    public static IReadOnlyList<RankedProduct> BestSellers(IEnumerable<Order> window, int limit) =>
        Ranked(SalesRanking.Rank(window, SalesRanking.ByProduct).Select(sold => sold.Key), limit);

    /// <summary>
    /// The products whose units grew most against the window before, among those
    /// that sold at least <see cref="TrendFloor"/> in this one.
    ///
    /// **Growth is the difference in units, not the ratio.** A ratio ranks 3
    /// against 1 (×3) above 40 against 20 (×2), and makes anything against zero
    /// infinite — the floor would be the only thing ranking. The difference says
    /// how many more people bought it, which is what "rising" means on a shop's
    /// home. Ties go to the one that sold more now, and then to the id, so the
    /// same orders always answer the same row. A product that sold the same or
    /// less is not rising, however much it sold: that is the best sellers' row.
    /// </summary>
    public static IReadOnlyList<RankedProduct> Trending(IEnumerable<Order> window, IEnumerable<Order> before, int limit)
    {
        var earlier = SalesRanking.Rank(before, SalesRanking.ByProduct)
            .ToDictionary(sold => sold.Key, sold => sold.Units, StringComparer.OrdinalIgnoreCase);

        return Ranked(
            SalesRanking.Rank(window, SalesRanking.ByProduct)
                .Where(sold => sold.Units >= TrendFloor)
                .Select(sold => (sold.Key, sold.Units, Growth: sold.Units - earlier.GetValueOrDefault(sold.Key)))
                .Where(rising => rising.Growth > 0)
                .OrderByDescending(rising => rising.Growth)
                .ThenByDescending(rising => rising.Units)
                .ThenBy(rising => rising.Key, StringComparer.Ordinal)
                .Select(rising => rising.Key),
            limit);
    }

    private static RankedProduct[] Ranked(IEnumerable<string> productIds, int limit) =>
        [.. productIds.Take(limit).Select((productId, index) => new RankedProduct(index + 1, productId))];
}

public sealed class WhatSellsHandlers(IShopHistory history, TimeProvider clock) :
    IQueryHandler<BestSellersQuery, RankedProducts>,
    IQueryHandler<TrendingQuery, RankedProducts>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<RankedProducts> HandleAsync(BestSellersQuery query, CancellationToken cancellationToken)
    {
        using var activity = Start("ordering.best_sellers", query.Days);
        var now = clock.GetUtcNow();

        var items = WhatSellsFigures.BestSellers(
            await history.OrdersPlacedAsync(now.AddDays(-query.Days), now, cancellationToken), query.Limit);

        activity?.SetTag("ordering.ranked", items.Count);
        return new RankedProducts(query.Days, items);
    }

    public async Task<RankedProducts> HandleAsync(TrendingQuery query, CancellationToken cancellationToken)
    {
        using var activity = Start("ordering.trending", query.Days);
        var now = clock.GetUtcNow();
        var from = now.AddDays(-query.Days);

        var items = WhatSellsFigures.Trending(
            await history.OrdersPlacedAsync(from, now, cancellationToken),
            await history.OrdersPlacedAsync(from.AddDays(-query.Days), from, cancellationToken),
            query.Limit);

        activity?.SetTag("ordering.ranked", items.Count);
        return new RankedProducts(query.Days, items);
    }

    private static Activity? Start(string name, int days)
    {
        var activity = Telemetry.StartActivity(name);
        activity?.SetTag("ordering.window_days", days);
        return activity;
    }
}

public sealed class WhatSellsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var rows = app.MapGroup("/api/ordering")
            // The home's rows, for anybody and any agent: a rank and an id, no figure.
            .AllowAnonymous()
            .WithTags("Ordering");

        // GET /api/ordering/best-sellers?days=7&limit=10
        rows.MapGet("/best-sellers",
            async Task<Ok<RankedProducts>> (int? days, int? limit, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(
                    new BestSellersQuery(days ?? WhatSellsWindow.DefaultDays, limit ?? WhatSellsWindow.DefaultLimit), ct)))
            .WithName("ListBestSellers");

        // GET /api/ordering/trending?days=7&limit=10
        rows.MapGet("/trending",
            async Task<Ok<RankedProducts>> (int? days, int? limit, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(
                    new TrendingQuery(days ?? WhatSellsWindow.DefaultDays, limit ?? WhatSellsWindow.DefaultLimit), ct)))
            .WithName("ListTrending");
    }
}
