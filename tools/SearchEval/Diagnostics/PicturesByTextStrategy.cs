using System.Diagnostics;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;

namespace ElGuerre.Tendero.SearchEval.Diagnostics;

/// <summary>
/// Words against the catalogue's pictures, alone: the products whose pictures
/// are nearest the query as SigLIP 2 embeds it, with the cards the lexical index
/// holds for them.
///
/// **A diagnostic, never a shop strategy** — as <see cref="VectorOnlyStrategy"/>
/// is for the words' embeddings. It answers the question the images spec leaves
/// open before text against pictures may become a leg of the search: what does
/// it rank, on the golden set the shop is measured on, when it ranks alone? It
/// has no thresholds and gates nothing.
///
/// No floor is applied: the ruler grades the ranking, and a floor would hide
/// exactly the tail it is here to show.
/// </summary>
internal sealed class PicturesByTextStrategy(IImageEmbeddingGenerator model, IPictureSearch pictures, ISearchHitReader cards) : ISearchStrategy
{
    public const string Name = "pictures";

    public async Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var matches = await pictures.NearestAsync(await model.EmbedTextAsync(query.Text, ct), query.Page * query.PageSize, ct);
        var similarity = matches.ToDictionary(match => match.ProductId, match => match.Similarity, StringComparer.Ordinal);

        // The cards come back in no promised order; the pictures' order is restored.
        var found = (await cards.FindAsync([.. similarity.Keys], query.Culture, query.Category, ct))
            .ToDictionary(hit => hit.ProductId, StringComparer.Ordinal);

        var ranked = matches
            .Where(match => found.ContainsKey(match.ProductId))
            .Select(match => found[match.ProductId] with { Score = similarity[match.ProductId] })
            .ToList();

        var page = ranked.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();

        return new SearchResultPage(page, ranked.Count, query.Page, query.PageSize, stopwatch.Elapsed.TotalMilliseconds);
    }
}
