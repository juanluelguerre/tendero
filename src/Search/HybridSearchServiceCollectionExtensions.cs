using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Answers;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Reranking;
using ElGuerre.Tendero.Search.Strategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Search;

public static class HybridSearchServiceCollectionExtensions
{
    /// <summary>
    /// Everything hybrid search needs on top of <c>AddLexicalSearch</c>: the vector
    /// layer, the reranker, and both strategies — each AI piece real when the
    /// AppHost handed it over and a named stand-in when it did not.
    /// </summary>
    public static IServiceCollection AddHybridSearch(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddVectorLayer(configuration);
        services.AddRerankerIfConfigured(configuration);
        services.AddQueryScope(QueryScopeCalibration.LoadEmbedded());

        services.AddOptions<HybridSearchOptions>().Bind(configuration.GetSection(HybridSearchOptions.Section));

        return services.AddSearchStrategies();
    }

    /// <summary>
    /// Layer B's classifier (ADR 0032), only where it can run AND has been
    /// measured: a committed calibration and an embedding generator. Anything
    /// less leaves <see cref="NoQueryScope"/>, which <see cref="AddSearchStrategies"/>
    /// registers — no flag, because a layer that has not earned its place has no
    /// switch to turn on.
    /// </summary>
    /// <param name="calibration">
    /// <see cref="QueryScopeCalibration.LoadEmbedded"/> in a real process; passed
    /// in so the registration can be proven without committing a calibration.
    /// </param>
    internal static IServiceCollection AddQueryScope(this IServiceCollection services, QueryScopeCalibration? calibration)
    {
        var canEmbed = services.Any(descriptor => descriptor.ServiceType == typeof(IEmbeddingGenerator<string, Embedding<float>>));
        if (calibration is null || !canEmbed)
            return services;

        services.Replace(ServiceDescriptor.Singleton<IQueryScope>(provider => new AnchorQueryScope(
            provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            provider.GetRequiredService<ICategoryReader>(),
            calibration,
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetRequiredService<ILogger<AnchorQueryScope>>())));

        return services;
    }

    /// <summary>
    /// The two strategies, keyed. Both are always registered: the one that answers is
    /// hybrid (ADR 0023, amended), lexical is what it degrades to, and the search
    /// evaluation asks both on purpose.
    /// </summary>
    public static IServiceCollection AddSearchStrategies(this IServiceCollection services)
    {
        services.AddOptions<HybridSearchOptions>();
        services.TryAddSingleton<IReranker, NoReranker>();
        services.TryAddSingleton<IQueryScope, NoQueryScope>();
        services.TryAddScoped<ISearchAnswerer, SearchAnswerer>();

        services.TryAddKeyedScoped<ISearchStrategy, LexicalSearchStrategy>(SearchStrategies.Lexical);
        services.TryAddKeyedScoped<ISearchStrategy, HybridSearchStrategy>(SearchStrategies.Hybrid);

        return services;
    }
}
