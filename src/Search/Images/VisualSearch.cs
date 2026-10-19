using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.Search.Images;

/// <param name="Product">The card, as a search result draws it — from the lexical index, which is the one that knows prices and stock.</param>
/// <param name="Picture">The picture of the product that was nearest: the colour that matched, not necessarily the card's own.</param>
/// <param name="Similarity">The cosine between what was asked and that picture. On the wire so a floor can be chosen from what shoppers saw.</param>
public sealed record VisualMatch(SearchHit Product, string Picture, double Similarity);

/// <param name="Items">Nearest first. Empty when nothing is near enough — a photograph of something the shop does not sell.</param>
public sealed record VisualSearchResult(IReadOnlyList<VisualMatch> Items);

/// <summary>Whether this shop can be searched by picture at all: configured, not probed.</summary>
/// <param name="Objects">
/// Whether the objects of a photograph can be found (the camera search, CS-2):
/// a detector is configured. Without one a photograph is searched whole, as one
/// object (CS-9).
/// </param>
public sealed record VisualSearchAvailability(bool Available, bool Objects);

public sealed class VisualSearchOptions
{
    public const string Section = "Search:Images";

    /// <summary>
    /// The least a catalogue picture must resemble the picture asked with to be
    /// an answer. A nearest neighbour always exists — the nearest thing to a
    /// photograph of a cat, in a shop with no cats, is still something — so
    /// without a floor every picture finds twelve products.
    /// </summary>
    public double MinPictureSimilarity { get; set; }

    /// <summary>
    /// The same floor for words against pictures, and a different number: a text
    /// and a picture of the same thing sit far lower than two pictures of it.
    /// </summary>
    public double MinTextSimilarity { get; set; }
}

/// <summary>
/// What the two visual questions share once each has its vector — a picture's or
/// a text's, from the one model that embeds both: the nearest products above a
/// floor, as the shop's cards.
///
/// It lives here and not in a slice because two slices ask it, and a slice never
/// references another.
/// </summary>
public sealed class VisualSearch(IPictureSearch pictures, IProductProfileReader profiles, ILexicalProductSearch? words = null)
{
    /// <summary>
    /// One policy for both questions: each runs a model on this process's CPU for
    /// whoever asks, signed in or not.
    /// </summary>
    public const string PerClient = "visual-search-per-client";

    public const int DefaultLimit = 12;

    public const int MaxLimit = 24;

    /// <summary>
    /// The most products a category and words narrow a picture to: the lexical
    /// index's first page this deep. The catalogue's largest branch is under a
    /// hundred; a shop with a branch past this would see its picture answered among
    /// the words' best five hundred, which is the cost written down rather than paid
    /// for (the scale rule).
    /// </summary>
    public const int NarrowingDepth = 500;

    /// <summary>
    /// The products a picture is to be answered among (the camera search, CS-4): those
    /// of the category, branch and all, that the lexical search matches for the words.
    /// Null when neither is given — every product. The picture leads; the words narrow.
    /// </summary>
    public async Task<IReadOnlyCollection<string>?> AmongAsync(string? text, string? category, string culture, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(category))
            return null;
        if (words is null)
            throw new InvalidOperationException("Narrowing a picture by words needs the lexical search, and none was given.");

        var page = await words.SearchAsync(new ProductSearchQuery(text?.Trim() ?? string.Empty, culture, Page: 1, PageSize: NarrowingDepth, Category: category), ct);
        return page.Hits.Select(hit => hit.ProductId).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<VisualSearchResult> NearestAsync(
        ReadOnlyMemory<float> vector, double floor, string culture, int limit, IReadOnlyCollection<string>? among = null, CancellationToken ct = default)
    {
        var near = (await pictures.NearestAsync(vector, limit, among, ct)).Where(match => match.Similarity >= floor).ToList();
        if (near.Count == 0)
            return new VisualSearchResult([]);

        var cards = (await profiles.ByIdsAsync([.. near.Select(match => match.ProductId)], culture, ct))
            .ToDictionary(profile => profile.ProductId, StringComparer.Ordinal);

        // In the pictures' order. A product the index does not hold — withdrawn
        // between its picture being embedded and now — is left out, not answered
        // as an error.
        return new VisualSearchResult(
        [
            .. near.Where(match => cards.ContainsKey(match.ProductId))
                .Select(match => new VisualMatch(cards[match.ProductId].Card, match.ImageId, match.Similarity)),
        ]);
    }
}
