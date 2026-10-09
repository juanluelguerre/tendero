using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// A picture, or a text to be compared with pictures, as a vector (phase 13a,
/// I-5): the port visual search stands on. One model embeds both, into one
/// space — that is what lets words find a picture — so the two are one port and
/// not two.
///
/// Every vector is unit length: the nearest picture is the largest dot product,
/// and whoever stores or compares them does not normalise again.
///
/// **Not the text search's model, and never mixed with it.** A product's words
/// are embedded by <c>bge-m3</c> into a collection named for it; these vectors
/// go to another, named for this model (<see cref="Model"/>), so the two spaces
/// cannot meet by accident.
/// </summary>
public interface IImageEmbeddingGenerator
{
    /// <summary>The model, the revision and the dimensions: what a collection of these vectors is named for.</summary>
    EmbeddingModelDescriptor Model { get; }

    /// <summary>
    /// Whether this process was started with the model. False is not a failure:
    /// the shop runs without visual search, and whoever would embed says so and
    /// does nothing (invariant 8).
    /// </summary>
    bool IsConfigured => true;

    /// <summary>A picture, as its encoded bytes — WebP, PNG, JPEG.</summary>
    /// <exception cref="ArgumentException">The bytes are not a picture this can decode.</exception>
    ValueTask<ReadOnlyMemory<float>> EmbedPictureAsync(ReadOnlyMemory<byte> picture, CancellationToken cancellationToken = default);

    /// <summary>A text, to be compared with pictures.</summary>
    ValueTask<ReadOnlyMemory<float>> EmbedTextAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>The port when no model was given: asked, it says the stage is not there, as the reranker's does.</summary>
public sealed class NoImageEmbeddings : IImageEmbeddingGenerator
{
    public const string Stage = "image embeddings";

    public EmbeddingModelDescriptor Model => EmbeddingModelDescriptor.Siglip2Base;

    public bool IsConfigured => false;

    public ValueTask<ReadOnlyMemory<float>> EmbedPictureAsync(ReadOnlyMemory<byte> picture, CancellationToken cancellationToken = default) =>
        throw new Contracts.SearchStageNotConfiguredException(Stage);

    public ValueTask<ReadOnlyMemory<float>> EmbedTextAsync(string text, CancellationToken cancellationToken = default) =>
        throw new Contracts.SearchStageNotConfiguredException(Stage);
}
