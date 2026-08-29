using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.DependencyInjection;
using Tendero.Search.Contracts;
using Tendero.Search.Elasticsearch;

namespace Tendero.Search;

public static class SearchServiceCollectionExtensions
{
    /// <summary>
    /// Búsqueda léxica: dependencia dura y modo degradado permanente (ADR 0004).
    /// Las capas de IA se enchufarán encima, nunca por debajo.
    /// </summary>
    public static IServiceCollection AddLexicalSearch(this IServiceCollection services, string endpoint)
    {
        services.AddSingleton(new ElasticsearchClient(
            new ElasticsearchClientSettings(new Uri(endpoint))));

        services.AddScoped<ILexicalProductSearch, ElasticsearchLexicalSearch>();
        services.AddScoped<IProductIndexer, ElasticsearchProductIndexer>();

        return services;
    }

    /// <summary>Crea products_es y products_en al arrancar. Sólo lo hospeda el
    /// worker: que dos réplicas de la API compitan por crear el índice no aporta nada.</summary>
    public static IServiceCollection AddSearchIndexInitializer(this IServiceCollection services)
    {
        services.AddHostedService<SearchIndexInitializer>();
        return services;
    }
}
