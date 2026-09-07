using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// The hosted adapter: Google's native image generation, the one everybody calls
/// "nano banana".
///
/// **There is no image endpoint.** It is the ordinary `generateContent` call with
/// `responseModalities` asking for an image as well as text, and the picture
/// comes back inline as base64 inside the response parts.
///
/// It exists beside the local one rather than instead of it, which is the same
/// arrangement the identity provider already has: a development issuer and
/// Keycloak behind one contract (ADR 0017). The local generator is the one that
/// works with no network, no account and no key, and that property is worth
/// keeping whatever the hosted one draws.
///
/// Three differences from SDXL are declared rather than papered over:
///
/// 1. **No seed.** Redrawing a product is a new roll, so `IsDeterministic` is
///    false and the tool says so in its report.
/// 2. **No negative prompt.** Classifier-free guidance has no analogue here, so
///    the negative is folded into the instruction as prose — which an
///    instruction-tuned model follows considerably better than CFG follows a bag
///    of words.
/// 3. **A key and a network.** Both are read from the environment and neither is
///    ever written anywhere: `GEMINI_API_KEY`, and nothing else.
/// </summary>
public sealed class GeminiIllustrator(string model, int size, string? apiKey = null) : IIllustrationGenerator
{
    private const string Host = "https://generativelanguage.googleapis.com/v1beta/models/";

    /// <summary>How many times to ask again when what came back is a tiled sheet
    /// rather than a product. The same three the local generator allows.</summary>
    private const int MaxAttempts = 3;

    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromMinutes(3) };

    public string Name => model;

    public bool IsDeterministic => false;

    public IReadOnlyList<GeneratedImage> Draw(
        IReadOnlyList<(string ItemId, string Prompt)> jobs, string negative, Action<string> say)
    {
        var key = apiKey ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new InvalidOperationException(
                """
                No GEMINI_API_KEY in the environment. Set one:

                  PowerShell:  $env:GEMINI_API_KEY = "..."
                  bash:        export GEMINI_API_KEY=...

                It is read from there and never written to a file, so it does not
                end up in the repository by accident.
                """);

        List<GeneratedImage> images = [];

        foreach (var (itemId, prompt) in jobs)
        {
            var clock = Stopwatch.StartNew();

            byte[] rgb = [];
            var drawn = 0;
            var attempt = 0;

            while (true)
            {
                say($"  {itemId}{(attempt > 0 ? $"  (attempt {attempt + 1})" : string.Empty)}");

                (rgb, drawn) = Ask(key, Instruction(prompt, negative));

                if (!SafeAreaProbe.Measure(rgb, drawn).LooksTiled(drawn) || ++attempt >= MaxAttempts)
                    break;

                say("    ink reaches every edge, which is a pattern and not a product. Asking again.");
            }

            clock.Stop();
            images.Add(Finished.From(itemId, rgb, drawn, clock.Elapsed));
        }

        return images;
    }

    /// <summary>
    /// The prompt as an instruction rather than as two bags of words. What SDXL
    /// needs a second, opposite prompt for, this one is simply told.
    /// </summary>
    private string Instruction(string prompt, string negative) =>
        $"""
         {prompt}

         Draw exactly one object and nothing else. Do not draw any text, letters,
         numbers, logos, brand marks, labels, badges or watermarks anywhere in the
         image, not even on the product itself. Do not draw furniture, a room, a
         surface, hands or a second object. Do not draw a mannequin, a person, a
         head, a torso or a stand: clothing is drawn as an empty garment holding
         its own shape, with nothing above the collar and nothing inside it.
         A feature named in the description is drawn as a SHAPE and never written
         as a word: a timer is a blank display, a rock plate is a moulded sole. The
         measurements in the description are facts about the product, never labels
         to print on it — never write a number or a unit anywhere on the object,
         on a sole, a strap, a display or a base.

         Fill every surface with solid colour. No surface is left unfilled or the
         same colour as the background: a white or cream object is drawn in light
         grey tones so that it reads against the background, never as an outline
         drawing. A pale object is built from clearly separated planes of light and
         mid grey, so its shape reads without relying on the outline, and whatever
         part of it is lit or functional carries real colour. Use the colour that
         is named, exactly as named — forest green is dark, not medium.

         Leave a wide empty margin above and below the object. Produce a square
         image {size} by {size} pixels.

         Avoid: {negative}
         """;

    private (byte[] Rgb, int Size) Ask(string key, string instruction)
    {
        var request = $$"""
            {
              "contents": [{ "parts": [{ "text": {{JsonSerializer.Serialize(instruction)}} }] }],
              "generationConfig": { "responseModalities": ["TEXT", "IMAGE"] }
            }
            """;

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{Host}{model}:generateContent")
        {
            Content = new StringContent(request, Encoding.UTF8, "application/json")
        };

        // In a header and not in the query string, because a key in a URL ends up
        // in every proxy log between here and there.
        message.Headers.Add("x-goog-api-key", key);

        using var response = _client.SendAsync(message).Result;
        var body = response.Content.ReadAsStringAsync().Result;

        // A refusal from a hosted model is a fact about an account, not a bug, and
        // it deserves the same treatment as an Elasticsearch that is not running:
        // say what happened and what to do about it.
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException(
                $"""
                 {model} refused on quota. The free tier limits requests per minute
                 and per day, and image generation spends both quickly.

                 Check what this key is actually allowed at:
                   https://ai.dev/rate-limit

                 Then wait for the window, or draw locally instead:
                   generate --provider sdxl --models <dir>
                 """);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"{model} answered {(int)response.StatusCode}: {body[..Math.Min(body.Length, 400)]}");

        using var document = JsonDocument.Parse(body);

        var inline = document.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")
            .EnumerateArray()
            .FirstOrDefault(part => part.TryGetProperty("inlineData", out _));

        if (inline.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException(
                $"""
                 {model} answered without an image. It said:

                 {body[..Math.Min(body.Length, 400)]}
                 """);

        var encoded = inline.GetProperty("inlineData").GetProperty("data").GetString() ?? string.Empty;

        return ToSquareRgb(Convert.FromBase64String(encoded));
    }

    /// <summary>
    /// Whatever came back, as the square of interleaved bytes the rest of the
    /// pipeline works in. A hosted model returns the size it feels like, so the
    /// shorter side wins and the picture is centre-cropped — the object is
    /// centred by instruction, so a crop takes background.
    /// </summary>
    private static (byte[] Rgb, int Size) ToSquareRgb(byte[] encoded)
    {
        using var bitmap = SKBitmap.Decode(encoded)
            ?? throw new InvalidDataException("The answer carried something that is not an image.");

        var side = Math.Min(bitmap.Width, bitmap.Height);
        var left = (bitmap.Width - side) / 2;
        var top = (bitmap.Height - side) / 2;

        var rgb = new byte[side * side * 3];

        for (var row = 0; row < side; row++)
            for (var column = 0; column < side; column++)
            {
                var pixel = bitmap.GetPixel(left + column, top + row);
                var at = ((row * side) + column) * 3;

                rgb[at] = pixel.Red;
                rgb[at + 1] = pixel.Green;
                rgb[at + 2] = pixel.Blue;
            }

        return (rgb, side);
    }

    public void Dispose() => _client.Dispose();
}
