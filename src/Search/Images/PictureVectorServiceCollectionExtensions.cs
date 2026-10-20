using ElGuerre.Tendero.Search.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElGuerre.Tendero.Search.Images;

public static class PictureVectorServiceCollectionExtensions
{
    /// <summary>
    /// The picture vectors when this process was handed a store AND the model's
    /// folder, and <see cref="NoPictureVectors"/> when it was not.
    ///
    /// Always registered one way or the other, as the words' layer is and for its
    /// reason: the handlers that project pictures are scanned by the worker
    /// whatever it was started with, and a missing registration would be a
    /// container that fails to build on every fresh clone.
    /// </summary>
    public static IServiceCollection AddPictureVectors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddImageEmbeddings(configuration);
        services.AddObjectDetection(configuration);
        services.AddQdrantPictureVectorsIfConfigured(configuration);

        services.TryAddSingleton<NoPictureVectors>();
        services.TryAddScoped<IPictureVectorIndexer>(provider => provider.GetRequiredService<NoPictureVectors>());
        services.TryAddScoped<IPictureSearch>(provider => provider.GetRequiredService<NoPictureVectors>());

        // What the two visual questions share, and the floors each answers above.
        services.AddOptions<VisualSearchOptions>().Bind(configuration.GetSection(VisualSearchOptions.Section));
        services.TryAddScoped<VisualSearch>();

        return services;
    }
}
