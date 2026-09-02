using ElGuerre.Tendero.Search.Contracts;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddScoped<IProductIndexer, ElasticsearchProductIndexer>();

        return services;
    }

    /// <summary>Creates products_es and products_en at startup. Only the worker
    /// hosts it: two API replicas racing to create the index adds nothing.</summary>
    public static IServiceCollection AddSearchIndexInitializer(this IServiceCollection services)
    {
        services.AddHostedService<SearchIndexInitializer>();
        return services;
    }
}
