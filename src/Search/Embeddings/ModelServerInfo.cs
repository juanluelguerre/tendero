using System.Net.Http.Json;
using System.Text.Json;

namespace ElGuerre.Tendero.Search.Embeddings;

/// <summary>What a model server says it loaded.</summary>
internal sealed record ServedModel(string ModelId, string Revision);

/// <summary>
/// Asks Text Embeddings Inference which model and revision it is serving.
///
/// It is the other half of pinning a revision (ADR 0031). The AppHost asks for
/// one, but a container started by hand, an old persistent container or a
/// different image can serve another — and vectors from two revisions of one
/// model look exactly as valid as vectors from one. The check that refuses them
/// needs the server to say what it holds, and <c>/info</c> is where TEI says it.
/// </summary>
internal sealed class ModelServerInfo(HttpClient http)
{
    public async Task<ServedModel> ReadAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync(new Uri("info", UriKind.Relative), ct);
        response.EnsureSuccessStatusCode();

        var info = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // A server started without --revision reports no sha, and that is an
        // answer: it is serving whatever `main` was on the day it downloaded.
        return new ServedModel(
            info.TryGetProperty("model_id", out var id) ? id.GetString() ?? "" : "",
            info.TryGetProperty("model_sha", out var sha) ? sha.GetString() ?? "" : "");
    }
}
