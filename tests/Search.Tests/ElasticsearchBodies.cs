using System.Text;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// A client whose every call fails at once, after its body is written down: what the
/// request-body tests read. The call never leaves the process. It used to go to a
/// port nothing listens on, and Windows refuses that in about four seconds — 68 s of
/// the suite's 107, run again for every mutant at a phase close.
/// </summary>
internal static class ElasticsearchBodies
{
    public static (ElasticsearchClient Client, List<string> Bodies) Capturing()
    {
        var bodies = new List<string>();
        var nothingListens = new InMemoryRequestInvoker(
            [], 0, new HttpRequestException("Nothing listens: only the body is read."), "application/json", []);

        var settings = new ElasticsearchClientSettings(new SingleNodePool(new Uri("http://127.0.0.1:9")), nothingListens)
            .DisableDirectStreaming()
            .OnRequestCompleted(call =>
            {
                if (call.RequestBodyInBytes is { Length: > 0 } body)
                    bodies.Add(Encoding.UTF8.GetString(body));
            });

        return (new ElasticsearchClient(settings), bodies);
    }
}
