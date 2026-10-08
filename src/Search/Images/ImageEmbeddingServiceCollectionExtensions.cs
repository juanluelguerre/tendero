using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Search.Images;

public static class ImageEmbeddingServiceCollectionExtensions
{
    /// <summary>The folder the export left the two towers and the vocabulary in (<c>prepare-siglip.py</c>).</summary>
    public const string FolderKey = "Search:Images:Models";

    /// <summary>
    /// The port, always: SigLIP 2 in process where a folder was given, and where
    /// none was, the adapter that says the stage is not there. Whoever embeds asks
    /// <see cref="IImageEmbeddingGenerator.IsConfigured"/> and takes its path
    /// without a model — a shop started without visual search is a shop, not an
    /// error (invariant 8).
    ///
    /// One instance for the process: each tower is loaded once, when it is first
    /// asked for, and ONNX Runtime runs it from any thread.
    /// </summary>
    public static IServiceCollection AddImageEmbeddings(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration[FolderKey] is { Length: > 0 } folder
            ? services.AddSingleton<IImageEmbeddingGenerator>(_ => new SiglipOnnxEmbeddings(folder))
            : services.AddSingleton<IImageEmbeddingGenerator, NoImageEmbeddings>();
    }
}
