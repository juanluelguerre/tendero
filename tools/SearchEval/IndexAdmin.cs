namespace ElGuerre.Tendero.SearchEval;

/// <summary>
/// The index operations the evaluation needs and that no port offers yet. They
/// go over plain HTTP on purpose: "the index is a disposable projection"
/// (docs/architecture.md) still has no rebuild operation. When reindexing is a
/// feature and not a need of this tool, it becomes a port and an ADR.
/// </summary>
public static class IndexAdmin
{
    /// <summary>
    /// Drops the indexes before evaluating. Essential for reproducibility: every
    /// run builds the aggregates in memory with fresh ProductIds, and without the
    /// drop the previous run's documents compete in the ranking.
    /// </summary>
    public static Task DropAsync(
        string elasticsearch, IEnumerable<string> indexNames, CancellationToken cancellationToken) =>
        ForEachIndexAsync(elasticsearch, indexNames, HttpMethod.Delete, path: "", cancellationToken);

    /// <summary>
    /// Makes the freshly indexed documents visible. Elasticsearch refreshes once
    /// a second by default, so without this the score depends on whether the
    /// refresh landed before or after the query: two runs of the same code gave
    /// 0.860 and 0.769. Waiting until "some" query returns something is not
    /// enough — it only proves ONE document is visible, not all six.
    /// </summary>
    public static Task RefreshAsync(
        string elasticsearch, IEnumerable<string> indexNames, CancellationToken cancellationToken) =>
        ForEachIndexAsync(elasticsearch, indexNames, HttpMethod.Post, "/_refresh", cancellationToken);

    /// <summary>
    /// Checks there is an Elasticsearch on the other side before starting.
    /// Without this, the first network failure comes out as a stack of nested
    /// exceptions that does not say the one thing that matters: nobody is
    /// listening.
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

            // A 404 on delete is the normal answer the first time.
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                continue;

            throw new InvalidOperationException(
                $"{method} on index '{index}' failed: {response.StatusCode}.");
        }
    }
}
