using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>What a vision model says about one illustration.</summary>
/// <param name="HasText">Any letter, word, number, logo or watermark. The
/// catalogue is bilingual, so a picture with a word in it is a picture that is
/// wrong in one of the two languages and cannot be translated.</param>
/// <param name="Colour">The dominant colour of the object, chosen from the nine
/// the catalogue declares plus "other". Six of the first eight illustrations
/// drifted from the colour their product declares, and a person found that by
/// looking.</param>
/// <param name="HasScene">Furniture, a room, a surface, or anything besides the
/// product itself. **This is the question pixels cannot answer**: a tiled sheet
/// fills the frame and is caught by counting, but a lamp standing on a desk
/// leaves a perfectly good margin.</param>
public sealed record VisionVerdict(bool HasText, string Colour, bool HasScene);

/// <summary>
/// The other half of the job, and the one Ollama is actually for.
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
public sealed class OllamaVisionClient(string url, string model)
{
    /// <summary>
    /// **snake_case, and not the repository's usual Web defaults.** The schema
    /// asks for `has_text`, `dominant_colour` and `has_scene`, and camelCase
    /// matching does not reach them — it does not fail either. It binds nothing,
    /// leaves every field at its default, and turns a correct answer into
    /// "no text, no scene, colour unknown" for every image. The model was right
    /// and the parser was quietly wrong, which took a raw request to see.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// **Deterministic on purpose.** A verdict that changes between Mondays is a
    /// gate nobody trusts, and this one decides whether a picture is regenerated.
    /// </summary>
    /// **`think: false` is not decoration.** The first model tried was a
    /// reasoning one, and with a small budget it spent the whole allowance
    /// thinking and returned an empty answer -- which parses as nothing and
    /// reads as a broken client.
    private const string Options = """"
        "think": false,
        "options": { "temperature": 0, "seed": 0, "num_predict": 512 }
        """";

    /// <summary>
    /// Closed questions with an enumerated answer, because open description is
    /// where these models wander. The colour list is generated from the
    /// catalogue's own options so it cannot drift from it.
    /// </summary>
    private static string Schema(IReadOnlyList<string> colours) =>
        $$"""
          {
            "type": "object",
            "properties": {
              "has_text": { "type": "boolean" },
              "dominant_colour": { "type": "string", "enum": [{{string.Join(", ", colours.Select(colour => $"\"{colour}\""))}}, "other"] },
              "has_scene": { "type": "boolean" }
            },
            "required": ["has_text", "dominant_colour", "has_scene"]
          }
          """;

    private const string Question =
        "Look at this product illustration and answer only about what is drawn in it. "
        + "has_text: is there any visible letter, word, number, logo, brand mark, label or watermark anywhere? "
        + "dominant_colour: what is the dominant colour of the product itself? "
        + "has_scene: is anything drawn besides the product alone — furniture, a desk, a table, "
        + "a floor, a wall, a room, a hand, or any second object?";

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
                 or point the tool elsewhere with --ollama <url>.
                 """, exception);
        }
    }

    public VisionVerdict Ask(byte[] imageBytes, IReadOnlyList<string> colours)
    {
        using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromMinutes(2) };

        var request = $$"""
            {
              "model": "{{model}}",
              "messages": [{
                "role": "user",
                "content": {{JsonSerializer.Serialize(Question)}},
                "images": ["{{Convert.ToBase64String(AsPng(imageBytes))}}"]
              }],
              "stream": false,
              "format": {{Schema(colours)}},
              {{Options}}
            }
            """;

        using var content = new StringContent(request, Encoding.UTF8, "application/json");
        using var response = client.PostAsync(new Uri("api/chat", UriKind.Relative), content).Result;

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result);
        var answer = document.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "{}";

        // A model's answer is data, not a contract. When it does not parse, the
        // message says what came back -- an opaque deserialisation stack tells
        // you nothing about the one thing that went wrong.
        try
        {
            return JsonSerializer.Deserialize<Answer>(answer, JsonOptions) is { } parsed
                ? new VisionVerdict(parsed.HasText, parsed.DominantColour ?? "other", parsed.HasScene)
                : new VisionVerdict(false, "other", false);
        }
        catch (JsonException failure)
        {
            throw new InvalidOperationException(
                $"""
                 '{model}' did not answer in the shape it was asked for. It said:

                 {answer[..Math.Min(answer.Length, 600)]}

                 If that reads like a refusal or a description, the model is
                 probably not multimodal -- try another with --model <name>.
                 """, failure);
        }
    }

    /// <summary>
    /// **WebP goes in and PNG comes out.** Ollama hands the bytes to a decoder
    /// that reads PNG, JPEG and a few others and does not read WebP — so the file
    /// this tool writes is exactly the one it cannot send.
    /// </summary>
    private static byte[] AsPng(byte[] imageBytes)
    {
        using var bitmap = SKBitmap.Decode(imageBytes)
            ?? throw new InvalidDataException("That is not an image this can decode.");

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return encoded.ToArray();
    }

    /// <summary>
    /// Free text about one image, for finding out whether a model is looking at
    /// it at all. Two different models answering "no scene" about a lamp on a
    /// desk is either two wrong models or one image that never arrived, and the
    /// difference matters.
    /// </summary>
    public string Describe(byte[] imageBytes, string? dumpTo = null)
    {
        var png = AsPng(imageBytes);

        if (dumpTo is not null)
            File.WriteAllBytes(dumpTo, png);

        using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromMinutes(2) };

        var request = $$"""
            {
              "model": "{{model}}",
              "messages": [{
                "role": "user",
                "content": "Describe what you see in this image in two sentences.",
                "images": ["{{Convert.ToBase64String(png)}}"]
              }],
              "stream": false,
              "think": false,
              "options": { "temperature": 0, "seed": 0, "num_predict": 200 }
            }
            """;

        using var content = new StringContent(request, Encoding.UTF8, "application/json");
        using var response = client.PostAsync(new Uri("api/chat", UriKind.Relative), content).Result;

        var body = response.Content.ReadAsStringAsync().Result;

        if (!response.IsSuccessStatusCode)
            return $"HTTP {(int)response.StatusCode}: {body}";

        using var document = JsonDocument.Parse(body);

        return document.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "(empty)";
    }

    private sealed record Answer(bool HasText, string? DominantColour, bool HasScene);
}
