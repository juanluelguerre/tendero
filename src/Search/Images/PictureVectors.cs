using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// The write port of the picture projection (phase 13a, P13-3): the catalogue's
/// pictures as vectors, kept by the rule every projection of a product keeps —
/// <see cref="ProductIndexProjection"/>: on sale is in, anything else is out.
///
/// **A picture is embedded once, whoever shows it.** The store is keyed on the
/// picture's id, which is the hash of its bytes: the same file under two
/// products is one point that names both, and a product that changes its price
/// eight times embeds nothing.
///
/// A port of its own, like the words' (<see cref="IProductVectorIndexer"/>):
/// every slice that asks for *the* index asks for exactly one.
/// </summary>
public interface IPictureVectorIndexer : IProductIndexer
{
    /// <summary>
    /// False on the stand-in. Asked by whoever would otherwise report a rebuild
    /// that wrote nothing as a success — the reindex somebody pressed — and by
    /// nobody on the outbox path, where writing nothing is the right answer.
    /// </summary>
    bool IsConfigured => true;
}

/// <summary>
/// The read port: the products whose pictures are nearest a vector — a picture's
/// or a text's, since one model embeds both (<see cref="IImageEmbeddingGenerator"/>).
/// It answers with ids and nothing else; a card is built from the lexical index,
/// which is the one that knows prices and stock.
///
/// Its failures are the vector layer's: a <see cref="SearchUnavailableException"/>
/// when the store is down, a <see cref="SearchStageNotConfiguredException"/> when
/// this process has none, and the caller survives either (invariant 8).
/// </summary>
public interface IPictureSearch
{
    /// <summary>The nearest products, nearest first, each once: by the nearest of its pictures.</summary>
    Task<IReadOnlyList<PictureMatch>> NearestAsync(ReadOnlyMemory<float> vector, int limit, CancellationToken ct = default);
}

/// <param name="ImageId">The picture of the product that was nearest: what a result may show.</param>
public sealed record PictureMatch(string ProductId, string ImageId, double Similarity);

/// <summary>
/// The picture layer of a process started without one, as a named type rather
/// than a missing registration — the vector layer's own answer
/// (<c>NoVectorLayer</c>): writing is a no-op, because there is nothing to keep
/// in step, and reading says the stage is not there.
/// </summary>
public sealed class NoPictureVectors : IPictureVectorIndexer, IPictureSearch
{
    public const string Stage = "picture vectors";

    /// <summary>What it tells whoever asked for something it cannot do.</summary>
    public const string Reason = "this process was started without a vector store and a picture model";

    public bool IsConfigured => false;

    public Task IndexAsync(Product product, CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveAsync(ProductId productId, CancellationToken ct = default) => Task.CompletedTask;

    public Task RecreateAsync(CancellationToken ct = default) =>
        throw new SearchUnavailableException("recreating the picture vectors", Reason);

    /// <summary>Never empty, so nothing ever sets out to rebuild a layer that does not exist.</summary>
    public Task<bool> IsEmptyAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task<IReadOnlyList<PictureMatch>> NearestAsync(ReadOnlyMemory<float> vector, int limit, CancellationToken ct = default) =>
        throw new SearchStageNotConfiguredException(Stage);
}
