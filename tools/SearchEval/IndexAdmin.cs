namespace Tendero.SearchEval;

/// <summary>
/// Operaciones sobre los índices que la evaluación necesita y que ningún puerto
/// ofrece todavía. Van por HTTP plano a propósito: el "índice es una proyección
/// desechable" (docs/architecture.md) aún no tiene operación de reconstrucción.
/// Cuando el reindexado sea una funcionalidad y no una necesidad de esta
/// herramienta, será un puerto y un ADR.
/// </summary>
public static class IndexAdmin
{
    /// <summary>
    /// Borra los índices antes de evaluar. Imprescindible para reproducibilidad:
    /// cada ejecución construye los agregados en memoria con ProductId nuevos, y
    /// sin borrar, los documentos de la pasada anterior compiten en el ranking.
    /// </summary>
    public static Task DropAsync(
        string elasticsearch, IEnumerable<string> indexNames, CancellationToken cancellationToken) =>
        ForEachIndexAsync(elasticsearch, indexNames, HttpMethod.Delete, path: "", cancellationToken);

    /// <summary>
    /// Hace visibles los documentos recién indexados. Elasticsearch refresca cada
    /// segundo por defecto, así que sin esto la puntuación depende de si el
    /// refresco cayó antes o después de la consulta: dos pasadas del mismo código
    /// daban 0.860 y 0.769. Esperar a que "alguna" consulta devuelva algo no
    /// basta — sólo prueba que UN documento es visible, no los seis.
    /// </summary>
    public static Task RefreshAsync(
        string elasticsearch, IEnumerable<string> indexNames, CancellationToken cancellationToken) =>
        ForEachIndexAsync(elasticsearch, indexNames, HttpMethod.Post, "/_refresh", cancellationToken);

    /// <summary>
    /// Comprueba que hay un Elasticsearch al otro lado antes de empezar. Sin
    /// esto, el primer fallo de red sale como una pila de excepciones anidadas
    /// que no dice lo único importante: que no hay nadie escuchando.
    /// </summary>
    public static async Task EnsureReachableAsync(string elasticsearch, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = new Uri(elasticsearch), Timeout = TimeSpan.FromSeconds(5) };

        try
        {
            using var response = await client.GetAsync(
                new Uri("_cluster/health", UriKind.Relative), cancellationToken);

            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException(
                $"No Elasticsearch answering at {elasticsearch}. Start one with:{Environment.NewLine}" +
                "  docker run -d --name tendero-es -p 9200:9200 \\" + Environment.NewLine +
                "    -e discovery.type=single-node -e xpack.security.enabled=false \\" + Environment.NewLine +
                "    docker.elastic.co/elasticsearch/elasticsearch:9.5.0" + Environment.NewLine +
                "or point the tool elsewhere with --elasticsearch <url>.", exception);
        }
    }

    private static async Task ForEachIndexAsync(
        string elasticsearch,
        IEnumerable<string> indexNames,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = new Uri(elasticsearch) };

        foreach (var index in indexNames)
        {
            using var request = new HttpRequestMessage(method, new Uri(index + path, UriKind.Relative));
            using var response = await client.SendAsync(request, cancellationToken);

            // 404 al borrar es la respuesta normal la primera vez.
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                continue;

            throw new InvalidOperationException(
                $"{method} on index '{index}' failed: {response.StatusCode}.");
        }
    }
}
