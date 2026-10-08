using System.Text;
using System.Text.Json;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// A vision model behind llama.cpp's own server, over the OpenAI-compatible chat
/// endpoint: **the model the shop runs**, started with its vision projector
/// (`--mmproj`). The check that accepts a picture is then made by the model that
/// will read it for alt text and claims (phase 13a), on weights converted by us
/// from the pinned revision (ADR 0031) — and it needs nothing installed on the
/// machine but the container the stack already uses.
///
/// A picture travels as a `data:` URL inside the message's content, and the
/// answer is held to the schema by the server's grammar (`response_format`).
/// </summary>
public sealed class LlamaVision(string url, string model) : IVisionModel
{
    public string Name => model;

    public void EnsureReachable()
    {
        using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(5) };

        try
        {
            using var response = client.GetAsync(new Uri("health", UriKind.Relative)).Result;
            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or AggregateException)
        {
            throw new InvalidOperationException(
                $"""
                 No llama.cpp server answering at {url}. Start the shop's model with its
                 vision projector (tools/SeedImages/README.md, "The vision pass"), or point
                 the tool elsewhere with --url <url>.
                 """, exception);
        }
    }

    public string Ask(IReadOnlyList<byte[]> pngs, string question, string schema) =>
        Chat(pngs, question, 512,
            $$"""
            , "response_format": { "type": "json_schema", "json_schema": { "name": "verdict", "strict": true, "schema": {{schema}} } }
            """) ?? "{}";

    public string Describe(byte[] png) =>
        Chat([png], "Describe what you see in this image in two sentences.", 200, "") ?? "(empty)";

    public string Answer(byte[] png, string question) => Chat([png], question, 16, "") ?? string.Empty;

    private string? Chat(IReadOnlyList<byte[]> pngs, string question, int maxTokens, string format)
    {
        var pictures = string.Join(", ", pngs.Select(png =>
            $$"""{ "type": "image_url", "image_url": { "url": "data:image/png;base64,{{Convert.ToBase64String(png)}}" } }"""));

        // The pictures first and the question after them, so "the first" and "the
        // second" in a question mean the order they were sent in.
        var request = $$"""
            {
              "model": {{JsonSerializer.Serialize(model)}},
              "messages": [{
                "role": "user",
                "content": [ {{pictures}}, { "type": "text", "text": {{JsonSerializer.Serialize(question)}} } ]
              }],
              "temperature": 0,
              "seed": 0,
              "max_tokens": {{maxTokens}}
              {{format}}
            }
            """;

        using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromMinutes(2) };
        using var content = new StringContent(request, Encoding.UTF8, "application/json");
        using var response = client.PostAsync(new Uri("v1/chat/completions", UriKind.Relative), content).Result;

        var body = response.Content.ReadAsStringAsync().Result;
        if (!response.IsSuccessStatusCode)
        {
            // A server started without its projector refuses a picture here, and
            // says so: that sentence is worth more than a status code.
            throw new InvalidOperationException(
                $"The llama.cpp server at {url} answered {(int)response.StatusCode}: {body[..Math.Min(body.Length, 400)]}");
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
    }
}
