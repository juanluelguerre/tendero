namespace ElGuerre.Tendero.Search.Contracts;

/// <summary>
/// Which embedding model produced a vector, pinned to the revision whose licence
/// was checked (ADR 0031).
///
/// A vector means nothing without it. Two models — or two revisions of one —
/// place the same sentence at different points, and a collection that mixes them
/// answers every query plausibly and wrongly, which is the failure no test of a
/// single call can see. So this is the one value three things agree on: the
/// collection is named after it, the startup check compares it with what the
/// model server says it loaded (P8-3), and a reindex targets it when it changes.
///
/// The revision is a full commit SHA and nothing else. A tag or <c>main</c> names
/// whatever the repository holds today, which is not what anybody checked.
/// </summary>
public sealed record EmbeddingModelDescriptor
{
    public EmbeddingModelDescriptor(string modelId, string revision, int dimensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);

        if (revision is not { Length: 40 } || !revision.All(char.IsAsciiHexDigitLower))
            throw new ArgumentException(
                $"'{revision}' is not a full lowercase commit SHA. A model is pinned to the revision that was checked, never to a tag or a branch.",
                nameof(revision));

        ModelId = modelId;
        Revision = revision;
        Dimensions = dimensions;
    }

    /// <summary>
    /// <c>BAAI/bge-m3</c>: MIT at this revision, 1024 dimensions, and one vector
    /// space for both cultures — measured at 0.793 between the Spanish and the
    /// English wording of the same coffee maker.
    /// </summary>
    public static EmbeddingModelDescriptor BgeM3 { get; } =
        new("BAAI/bge-m3", "5617a9f61b028005a4858fdac845db406aefb181", 1024);

    /// <summary>The repository id on Hugging Face, <c>organisation/name</c>.</summary>
    public string ModelId { get; }

    /// <summary>The full commit SHA the weights are served from.</summary>
    public string Revision { get; }

    /// <summary>The length of every vector this model produces.</summary>
    public int Dimensions { get; }

    /// <summary>
    /// The vector collection this model writes to, carrying the model, the
    /// revision and the dimensions — <c>products_bge-m3_5617a9f_1024</c>. A
    /// different model or revision gets a different collection by construction,
    /// so a mixed vector space is impossible rather than merely unlikely.
    /// </summary>
    public string CollectionName =>
        $"products_{NameOf(ModelId)}_{Revision[..7]}_{Dimensions}";

    private static string NameOf(string modelId)
    {
        var name = modelId[(modelId.LastIndexOf('/') + 1)..].ToLowerInvariant();
        return string.Concat(name.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-'));
    }
}
