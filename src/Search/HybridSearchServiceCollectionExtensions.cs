using ElGuerre.Tendero.Search.Answers;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Reranking;
using ElGuerre.Tendero.Search.Strategies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.AddOptions<HybridSearchOptions>().Bind(configuration.GetSection(HybridSearchOptions.Section));

        return services.AddSearchStrategies();
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
        services.TryAddScoped<ISearchAnswerer, SearchAnswerer>();

        services.TryAddKeyedScoped<ISearchStrategy, LexicalSearchStrategy>(SearchStrategies.Lexical);
        services.TryAddKeyedScoped<ISearchStrategy, HybridSearchStrategy>(SearchStrategies.Hybrid);

        return services;
    }
}
