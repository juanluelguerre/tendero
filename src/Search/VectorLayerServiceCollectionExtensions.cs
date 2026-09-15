using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Embeddings;
using ElGuerre.Tendero.Search.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElGuerre.Tendero.Search;

public static class VectorLayerServiceCollectionExtensions
{
    /// <summary>
    /// The vector layer when the AppHost handed this process one, and
    /// <see cref="NoVectorLayer"/> when it did not.
    ///
    /// Always registered one way or the other, because the handlers that use it
    /// are scanned by the API and by the worker regardless: a missing registration
    /// would be a container that fails to build on a machine that never started
    /// Qdrant, which is every fresh clone.
    /// </summary>
    public static IServiceCollection AddVectorLayer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddProductEmbeddingsIfConfigured(configuration);
        services.AddQdrantVectorsIfConfigured(configuration);

        services.TryAddSingleton<NoVectorLayer>();
        services.TryAddSingleton<IVectorLayer>(provider => provider.GetRequiredService<NoVectorLayer>());
        services.TryAddScoped<IProductVectorIndexer>(provider => provider.GetRequiredService<NoVectorLayer>());
        services.TryAddScoped<IVectorProductSearch>(provider => provider.GetRequiredService<NoVectorLayer>());

        return services;
    }
}
