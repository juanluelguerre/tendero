using System.Text.Json;
using SkiaSharp;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// Whatever looks at the pictures.
///
/// **A port with keyed adapters, as the generator is** (ADR 0003): pictures and a
/// question go in, an answer held to a schema comes out. What is asked, how the
/// answer is read and what it means for a picture are shared
/// (<see cref="VisionChecks"/>); an adapter only knows how its server wants a
/// picture sent. Two of them: Ollama, where this started, and llama.cpp's own
/// server — the one the shop runs its model on, so the check that accepts a
/// picture is made by the model that will later read it (phase 13a, I-2).
/// </summary>
public interface IVisionModel
{
    /// <summary>For the report: which model said so.</summary>
    string Name { get; }

    /// <summary>Fails with what to run when nothing answers, before the first picture and not on it.</summary>
    void EnsureReachable();

    /// <summary>The model's answer to <paramref name="question"/> about the pictures, as JSON of <paramref name="schema"/>.</summary>
    /// <param name="pngs">The pictures, in the order the question names them. PNG: neither server decodes WebP.</param>
    string Ask(IReadOnlyList<byte[]> pngs, string question, string schema);

    /// <summary>Free text about one picture: for finding out whether a model is looking at it at all.</summary>
    string Describe(byte[] png);
}

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

/// <summary>Whether a picture shows the product its data describes (phase 13a, I-1).</summary>
/// <param name="Shows">What the model sees, in a few words, before it is told what to expect of it.</param>
/// <param name="Matches">Whether that is the product described.</param>
/// <param name="Differs">What differs — a count, a shape, a material, a part the description names — or empty.</param>
public sealed record DescriptionVerdict(string Shows, bool Matches, string Differs);

/// <summary>Whether a picture is drawn as a reference picture is (phase 13a, I-1).</summary>
/// <param name="SameStyle">The same kind of drawing: flat illustration with outlines, not a photograph or a render.</param>
/// <param name="SameBackground">The same plain, light background with nothing on it.</param>
/// <param name="Differs">What differs, or empty.</param>
public sealed record RegisterVerdict(bool SameStyle, bool SameBackground, string Differs);

/// <summary>
/// The questions a picture is asked, whichever model answers them. Closed
/// questions with an enumerated or boolean answer, because open description is
/// where these models wander; temperature zero, because a verdict that changes
/// between Mondays is a gate nobody trusts.
///
/// **Every verdict here is a proposal.** It marks a picture for a person to look
/// at; it never accepts one and never corrects the catalogue (the author's rule,
/// 2026-10-05).
/// </summary>
public sealed class VisionChecks(IVisionModel model)
{
    /// <summary>
    /// **snake_case, and not the repository's usual Web defaults.** The schemas
    /// ask for `has_text`, `dominant_colour` and `has_scene`, and camelCase
    /// matching does not reach them — it does not fail either. It binds nothing,
    /// leaves every field at its default, and turns a correct answer into
    /// "no text, no scene, colour unknown" for every image. The model was right
    /// and the parser was quietly wrong, which took a raw request to see.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private const string LookQuestion =
        "Look at this product illustration and answer only about what is drawn in it. "
        + "has_text: is there any visible letter, word, number, logo, brand mark, label, symbol or watermark anywhere? "
        + "dominant_colour: what is the dominant colour of the product itself? "
        + "has_scene: is anything drawn besides the product alone — furniture, a desk, a table, "
        + "a floor, a wall, a room, a hand, or any second object?";

    /// <summary>
    /// The colour list is generated from the catalogue's own options so it cannot
    /// drift from it.
    /// </summary>
    public VisionVerdict Look(byte[] image, IReadOnlyList<string> colours)
    {
        var schema = $$"""
            {
              "type": "object",
              "properties": {
                "has_text": { "type": "boolean" },
                "dominant_colour": { "type": "string", "enum": [{{string.Join(", ", colours.Select(colour => $"\"{colour}\""))}}, "other"] },
                "has_scene": { "type": "boolean" }
              },
              "required": ["has_text", "dominant_colour", "has_scene"],
              "additionalProperties": false
            }
            """;

        var answer = Read<LookAnswer>(model.Ask([AsPng(image)], LookQuestion, schema));
        return new VisionVerdict(answer.HasText, answer.DominantColour ?? "other", answer.HasScene);
    }

    private const string DescriptionSchema = """
        {
          "type": "object",
          "properties": {
            "shows": { "type": "string" },
            "matches": { "type": "boolean" },
            "differs": { "type": "string" }
          },
          "required": ["shows", "matches", "differs"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Whether the picture shows what the product's name and description say.
    /// "shows" is asked first, so the model says what it sees before it reads what
    /// it is expected to see; "differs" names a count, a shape, a material or a
    /// part — the things a drawing gets wrong while still being a shoe.
    /// </summary>
    public DescriptionVerdict Against(byte[] image, string name, string description)
    {
        var question =
            "Look at this product illustration. "
            + "shows: say in a few words what object is drawn. "
            + $"The shop sells it as \"{name}\", described as: \"{description}\" "
            + "matches: is the object drawn that product? Judge the kind of object, how many there are, its shape, "
            + "and the parts the description names. Ignore what a drawing cannot show: weight, size, price, how it is washed. "
            + "differs: if it does not match, what differs, in a few words; otherwise an empty string.";

        var answer = Read<DescriptionAnswer>(model.Ask([AsPng(image)], question, DescriptionSchema));
        return new DescriptionVerdict(answer.Shows ?? "", answer.Matches, answer.Differs ?? "");
    }

    private const string RegisterSchema = """
        {
          "type": "object",
          "properties": {
            "same_style": { "type": "boolean" },
            "same_background": { "type": "boolean" },
            "differs": { "type": "string" }
          },
          "required": ["same_style", "same_background", "differs"],
          "additionalProperties": false
        }
        """;

    private const string RegisterQuestion =
        "Two product illustrations of DIFFERENT products: the first is the reference, the second is the candidate. "
        + "Do not compare the products; compare how they are drawn. "
        + "same_style: is the candidate drawn in the same style as the reference — a flat illustration with clean outlines "
        + "and flat colours, not a photograph, not a 3D render, not a sketch? "
        + "same_background: does the candidate have the same kind of background — plain, light, empty, with at most a soft shadow? "
        + "differs: what differs in how it is drawn, in a few words; otherwise an empty string.";

    /// <summary>Whether the candidate is drawn as the reference is: the catalogue has one register (the first eight set it).</summary>
    public RegisterVerdict Beside(byte[] candidate, byte[] reference)
    {
        var answer = Read<RegisterAnswer>(model.Ask([AsPng(reference), AsPng(candidate)], RegisterQuestion, RegisterSchema));
        return new RegisterVerdict(answer.SameStyle, answer.SameBackground, answer.Differs ?? "");
    }

    public string Describe(byte[] image, string? dumpTo = null)
    {
        var png = AsPng(image);
        if (dumpTo is not null)
            File.WriteAllBytes(dumpTo, png);

        return model.Describe(png);
    }

    /// <summary>
    /// A model's answer is data, not a contract. When it does not parse, the
    /// message says what came back — an opaque deserialisation stack tells you
    /// nothing about the one thing that went wrong.
    /// </summary>
    private T Read<T>(string answer)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(answer, JsonOptions)
                ?? throw new JsonException("The answer was null.");
        }
        catch (JsonException failure)
        {
            throw new InvalidOperationException(
                $"""
                 '{model.Name}' did not answer in the shape it was asked for. It said:

                 {answer[..Math.Min(answer.Length, 600)]}

                 If that reads like a refusal or a description, the model is
                 probably not multimodal -- try another with --model <name>.
                 """, failure);
        }
    }

    /// <summary>
    /// **WebP goes in and PNG comes out.** Both servers hand the bytes to a decoder
    /// that reads PNG, JPEG and a few others and does not read WebP — so the file
    /// this tool writes is exactly the one it cannot send.
    /// </summary>
    public static byte[] AsPng(byte[] imageBytes)
    {
        // Asked of the codec, not of SKBitmap.Decode: handed bytes that are no
        // image, that one throws an ArgumentNullException about a parameter of its
        // own instead of returning nothing — which a test found, not a reader.
        using var data = SKData.CreateCopy(imageBytes);
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidDataException("That is not an image this can decode.");
        using var bitmap = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException("That is not an image this can decode.");

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return encoded.ToArray();
    }

    private sealed record LookAnswer(bool HasText, string? DominantColour, bool HasScene);

    private sealed record DescriptionAnswer(string? Shows, bool Matches, string? Differs);

    private sealed record RegisterAnswer(bool SameStyle, bool SameBackground, string? Differs);
}
