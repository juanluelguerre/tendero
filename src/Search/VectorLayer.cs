using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Strategies;

namespace ElGuerre.Tendero.Search.Contracts;

/// <summary>
/// The write port of the vector projection (P8-4).
///
/// The same four operations as the lexical index, because it is the same
/// projection of the same products — Active is embedded, everything else is
/// removed — kept by the same rule, <see cref="ProductIndexProjection"/>. If the
/// two had rules of their own, a product could be findable by meaning and not by
/// its name, and the difference would only show up by searching.
///
/// It is a port of its own rather than a second registration of
/// <see cref="IProductIndexer"/>: every slice that asks for the index asks for
/// exactly one, and two implementations of one interface turn "the index" into
/// "whichever was registered last".
/// </summary>
public interface IProductVectorIndexer : IProductIndexer;

/// <summary>
/// The read port of the vector layer. It answers with product ids and nothing
/// else: a card is built from the lexical index, which is the one that knows
/// prices and stock, so the two can never disagree about either.
///
/// A real failure — the model server down, a mismatch — is a
/// <see cref="SearchUnavailableException"/>. A process started with no vector
/// layer at all throws <see cref="SearchStageNotConfiguredException"/> instead
/// (<see cref="NoVectorLayer"/>): a different fact, so the hybrid strategy can
/// tell "never had one" from "has one and it is down" without guessing from a
/// message. Either way the caller is expected to survive it (invariant 8).
/// </summary>
public interface IVectorProductSearch
{
    /// <summary>The products nearest in meaning to <paramref name="text"/>, nearest first.</summary>
    Task<IReadOnlyList<VectorMatch>> SearchAsync(
        string text, string culture, string? category, int limit, CancellationToken ct = default);

    /// <summary>
    /// The text each product was embedded from, in one culture. The reranker reads
    /// it: a card's name alone is too little for a model to judge, and asking
    /// Postgres for descriptions per search would put the catalogue in the query
    /// path of a feature that is allowed to fail.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> TextsAsync(
        IReadOnlyCollection<string> productIds, string culture, CancellationToken ct = default);
}

public sealed record VectorMatch(string ProductId, double Similarity);

/// <summary>
/// Whether the vector layer can be used right now, and why not when it cannot.
///
/// **A mismatch is not an exception, it is a status** (P8-3). The model server
/// serving a different revision, or a collection holding vectors of another
/// size, means every similarity it would compute is a plausible wrong number —
/// so the layer declares itself unavailable and search degrades to lexical. A
/// model bump must never take the shop down, and it must never quietly mix two
/// vector spaces either.
/// </summary>
public interface IVectorLayer
{
    Task<VectorLayerStatus> CheckAsync(CancellationToken ct = default);
}

public sealed record VectorLayerStatus(bool IsConfigured, bool IsAvailable, string Reason)
{
    public static readonly VectorLayerStatus NotConfigured =
        new(IsConfigured: false, IsAvailable: false, "this process was started without a model server and a vector store");

    public static VectorLayerStatus Available(string description) => new(true, true, description);

    public static VectorLayerStatus Unavailable(string reason) => new(true, false, reason);
}

/// <summary>
/// The vector layer of a process started without one, as a named type rather than
/// a missing registration.
///
/// The projection handlers are scanned by the API and by the worker, so they have
/// to resolve in both whether or not anybody started Qdrant. Writing is a no-op —
/// there is nothing to keep in step — and reading throws
/// <see cref="SearchStageNotConfiguredException"/>, which the hybrid strategy
/// treats as a stage to skip rather than a degradation to count.
/// </summary>
internal sealed class NoVectorLayer : IVectorLayer, IProductVectorIndexer, IVectorProductSearch
{
    public Task<VectorLayerStatus> CheckAsync(CancellationToken ct = default) =>
        Task.FromResult(VectorLayerStatus.NotConfigured);

    public Task IndexAsync(Product product, CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveAsync(ProductId productId, CancellationToken ct = default) => Task.CompletedTask;

    public Task RecreateAsync(CancellationToken ct = default) => throw Refusal("recreating the vectors");

    /// <summary>Never empty, so nothing ever sets out to rebuild a layer that does not exist.</summary>
    public Task<bool> IsEmptyAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task<IReadOnlyList<VectorMatch>> SearchAsync(
        string text, string culture, string? category, int limit, CancellationToken ct = default) =>
        throw new SearchStageNotConfiguredException(SearchDegradation.VectorStage);

    public Task<IReadOnlyDictionary<string, string>> TextsAsync(
        IReadOnlyCollection<string> productIds, string culture, CancellationToken ct = default) =>
        throw new SearchStageNotConfiguredException(SearchDegradation.VectorStage);

    private static SearchUnavailableException Refusal(string operation) =>
        new(operation, VectorLayerStatus.NotConfigured.Reason);
}
