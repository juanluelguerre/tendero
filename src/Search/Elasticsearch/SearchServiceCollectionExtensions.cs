using Elastic.Clients.Elasticsearch;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Offers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// The namespace matters: architecture rule 4 says the Elastic client is not
// visible outside Tendero.Search.Elasticsearch, and composing those services is
// precisely Elasticsearch code.
namespace ElGuerre.Tendero.Search.Elasticsearch;

public static class SearchServiceCollectionExtensions
{
    /// <summary>
    /// Lexical search: a hard dependency and the permanent degraded mode (ADR
    /// 0004). The AI layers plug in on top, never underneath.
    /// </summary>
    public static IServiceCollection AddLexicalSearch(this IServiceCollection services, string endpoint)
    {
        services.AddSingleton(new ElasticsearchClient(
            new ElasticsearchClientSettings(new Uri(endpoint))));

        services.AddScoped<ILexicalProductSearch, ElasticsearchLexicalSearch>();

        // "On offer" is asked of Pricing when a search runs, never indexed (the
        // facets spec). A process without Pricing — the worker, the search
        // evaluation — gets nothing on offer rather than a container that will
        // not build (PricingRunningOffers says how).
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IRunningOffers, PricingRunningOffers>();
        services.AddScoped<IProductIndexer, ElasticsearchProductIndexer>();
        services.AddScoped<ISearchIndexSchema, ElasticsearchIndexSchema>();

        // Hybrid search builds every card from here, so it is part of the lexical
        // layer rather than of the AI one: it answers whether or not Qdrant does.
        services.AddScoped<ISearchHitReader, ElasticsearchHitReader>();
        services.AddScoped<ILexicalFullMatch, ElasticsearchFullMatch>();

        // The home's rows read products whole — the trail's cards, the best
        // sellers' cards, the neighbours of what was viewed (R-8) — and, like the
        // cards above, answer whether or not the AI layer does.
        services.AddScoped<IProductProfileReader, ElasticsearchProductProfiles>();

        return services;
    }

    /// <summary>Creates products_es and products_en at startup. Only the worker
    /// hosts it: two API replicas racing to create the index adds nothing.</summary>
    public static IServiceCollection AddSearchIndexInitializer(this IServiceCollection services)
    {
        services.AddHostedService<CreateSearchIndexesAtStartupWorker>();
        return services;
    }
}
