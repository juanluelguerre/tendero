using System.Text;
using System.Text.Json;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// A vision model behind Ollama, where the checks started.
///
/// It does not generate anything — it never could, and assuming otherwise is
/// where this whole feature started. What a local vision model does well is look
/// at a finished picture and answer closed questions about it, which is exactly
/// the check that was missing: `seed/IMAGES-TODO.md` records that six of the
/// first eight illustrations drifted from their declared colour, and the way
/// that was discovered was somebody noticing.
///
/// **The image goes on a chat message, not on a completion.** `/api/generate`
/// accepts an `images` field, answers 200, and quietly ignores it — the model
/// then describes an image it never received, which is a confident hallucination
/// and not an error. Two different models were suspected before the request was.
/// `/api/chat` carries it.
///
/// **It runs in its own pass**, never beside the generator. A vision model is
/// gigabytes and so is the UNet, and interleaving them on one card would thrash
/// it. Generate a batch, then look at the batch.
/// </summary>
public sealed class OllamaVision(string url, string model) : IVisionModel
{
    public string Name => model;

    /// <summary>
    /// **Deterministic on purpose**, and **`think: false` is not decoration.** The
    /// first model tried was a reasoning one, and with a small budget it spent the
    /// whole allowance thinking and returned an empty answer — which parses as
    /// nothing and reads as a broken client.
    /// </summary>
    private const string Options = """
        "think": false,
        "options": { "temperature": 0, "seed": 0, "num_predict": 512 }
        """;

    /// <summary>
    /// The reachability probe, in the shape `IndexAdmin` established: a short
    /// timeout, and a failure that names the command that fixes it rather than
    /// the exception that caused it.
    /// </summary>
    public void EnsureReachable()
    {
        using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(5) };

        try
        {
            using var response = client.GetAsync(new Uri("api/tags", UriKind.Relative)).Result;
            var body = response.Content.ReadAsStringAsync().Result;

            // Checked before the first image and not on the first failure: a pass
            // over ninety-two pictures that dies on the first one because of a
            // missing pull is a bad afternoon.
            if (!body.Contains(model.Split(':')[0], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"""
                     Ollama is running at {url} but has no '{model}'. Pull it with:
                       ollama pull {model}
                     or name another with --model <name>.
                     """);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or AggregateException)
        {
            throw new InvalidOperationException(
                $"""
                 No Ollama answering at {url}. Start one with:
                   ollama serve
                 and pull a vision model with:
                   ollama pull {model}
                 or point the tool elsewhere with --url <url>, or at the shop's own model with --provider llama.
                 """, exception);
        }
    }

    public string Ask(IReadOnlyList<byte[]> pngs, string question, string schema) =>
        Chat($$"""
            {
              "model": "{{model}}",
              "messages": [{
                "role": "user",
                "content": {{JsonSerializer.Serialize(question)}},
                "images": [{{Images(pngs)}}]
              }],
              "stream": false,
              "format": {{schema}},
              {{Options}}
            }
            """) ?? "{}";

    public string Describe(byte[] png) =>
        Chat($$"""
            {
              "model": "{{model}}",
              "messages": [{
                "role": "user",
                "content": "Describe what you see in this image in two sentences.",
                "images": [{{Images([png])}}]
              }],
              "stream": false,
              "think": false,
              "options": { "temperature": 0, "seed": 0, "num_predict": 200 }
            }
            """) ?? "(empty)";

    public string Answer(byte[] png, string question) =>
        Chat($$"""
            {
              "model": "{{model}}",
              "messages": [{
                "role": "user",
                "content": {{JsonSerializer.Serialize(question)}},
                "images": [{{Images([png])}}]
              }],
              "stream": false,
              "think": false,
              "options": { "temperature": 0, "seed": 0, "num_predict": 16 }
            }
            """) ?? string.Empty;

    private static string Images(IReadOnlyList<byte[]> pngs) =>
        string.Join(", ", pngs.Select(png => $"\"{Convert.ToBase64String(png)}\""));

    private string? Chat(string request)
    {
        using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromMinutes(2) };
        using var content = new StringContent(request, Encoding.UTF8, "application/json");
        using var response = client.PostAsync(new Uri("api/chat", UriKind.Relative), content).Result;

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result);
        return document.RootElement.GetProperty("message").GetProperty("content").GetString();
    }
}
