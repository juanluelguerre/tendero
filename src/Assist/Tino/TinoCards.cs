using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Tino;

/// <summary>
/// The cards of an answer, drawn by code from what the model said and what the
/// turn's searches found (2026-10-21). Asked for coffee makers between 31 and 50 €,
/// the local model named one, the one that could not be bought, and the answer
/// showed one card (the author's walk, 2026-10-14). Which products the model speaks
/// of is the model's; how many cards, which ones and in what order is code's
/// (invariant 12):
///
/// <list type="number">
/// <item>the products the model gave a reason for that held;</item>
/// <item>those it named and said nothing of, when a search found them for what was asked;</item>
/// <item>up to three, what the searches found in the category of the products the model
/// named — never another category, so a coffee maker's answer is never topped up with
/// the pan the same search found — the ones that can be bought first.</item>
/// </list>
///
/// The ones that can be bought come first among the cards, each group in its order.
/// A card with no reason carries the one line code knows is true of it (option C,
/// the author, 2026-10-21). A product every reason of which fell is never brought
/// back: the model got it wrong, and a line of the shop's under it would cover that up.
/// </summary>
public static class TinoCards
{
    public static IReadOnlyList<AnsweredProduct> Of(TinoBody body, FactSheet sheet, string culture)
    {
        var chosen = body.Products.Where(product => product.Reasons.Count > 0)
            .Concat(body.Products.Where(product => product.Reasons.Count == 0 && sheet.Found(product.Code) is not null))
            .Take(AnswerSchema.MaxProducts)
            .ToList();

        // The category is the shop's fact about a product, whatever the model said of
        // it: a product whose reasons fell still says what kind of thing was asked for.
        HashSet<string> categories =
        [
            with(StringComparer.OrdinalIgnoreCase),
            .. body.Products.Select(product => product.Code).Concat(body.Fell)
                .Select(code => sheet.Found(code)?.Category).OfType<string>(),
        ];
        HashSet<string> taken = [with(StringComparer.OrdinalIgnoreCase), .. chosen.Select(card => card.Code), .. body.Fell];

        var added = sheet.Searches.SelectMany(search => search.Products)
            .DistinctBy(found => found.Code, StringComparer.OrdinalIgnoreCase)
            .Where(found => found.Category is { } category && categories.Contains(category) && !taken.Contains(found.Code))
            .OrderBy(found => !found.InStock)
            .Take(AnswerSchema.MaxProducts - chosen.Count)
            .Select(found => new AnsweredProduct(found.Code, sheet.Product(found.Code)?.Name ?? found.Code, []));

        return
        [
            .. chosen.Concat(added)
                .Select(card => card.Reasons.Count > 0
                    ? card
                    : card with { Note = new Message<ProductNote>(ProductNote.MatchesSearch, card.Code, TinoPrompt.MatchesSearch(culture)) })
                .OrderBy(card => sheet.Found(card.Code) is { InStock: false }),
        ];
    }

    /// <summary>
    /// Where "see all" leads from the cards: the last search that found one of them, as
    /// it was run — inside the cards' category when they share one and the search named
    /// none, a filter the listing shows and the shopper can take off — and how many
    /// products that holds. None when the cards are all there is.
    /// </summary>
    public static SeeAll? SeeAll(IReadOnlyList<AnsweredProduct> cards, FactSheet sheet)
    {
        if (sheet.Searches.LastOrDefault(search => search.Products.Any(found => cards.Any(card => Same(card.Code, found.Code))))
            is not { } search)
            return null;

        var shared = cards.Select(card => sheet.Found(card.Code)?.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var more = search.Search.Category is null && shared is [{ } category]
            ? new SeeAll(search.Search with { Category = category },
                search.PerCategory.TryGetValue(category, out var held)
                    ? held
                    : search.Products.Count(found => Same(found.Category, category)))
            : new SeeAll(search.Search, search.Total);

        return more.Total > cards.Count ? more : null;
    }

    private static bool Same(string? one, string? other) => string.Equals(one, other, StringComparison.OrdinalIgnoreCase);
}
