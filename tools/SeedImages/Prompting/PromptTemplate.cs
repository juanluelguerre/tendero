namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// What the model is asked for, built from the register published in
/// <c>seed/IMAGES-TODO.md</c>.
///
/// **The subject line comes first, and that is a change from the document's
/// original order.** CLIP takes 77 tokens and silently discards the rest; the
/// style block is long and carries six hex codes, and the CLIP tokenizer splits
/// numbers one digit at a time, so those alone cost more than a sentence. With
/// the subject last — which is where a human would naturally put it — the colour
/// and the product are exactly what falls off the end, and the result is ninety
/// two competent illustrations of nothing in particular.
///
/// Two lines were also dropped rather than reordered. `1400 x 1400 pixels, PNG`
/// and `sRGB` describe the FILE, and the file is SkiaSharp's business: SDXL
/// emits 1024 square whatever you tell it. They were spending tokens to ask for
/// something the model cannot give and does not need to.
/// </summary>
public static class PromptTemplate
{
    /// <summary>
    /// The register, minus the subject. Published verbatim in
    /// <c>seed/IMAGES-TODO.md</c>, and a test asserts every line is still there —
    /// the document is what a person reads to know what the hundred images are
    /// supposed to look like, and code and prose drifting means the prose wins
    /// while being wrong.
    /// </summary>
    public static readonly string[] StyleLines =
    [
        "Flat vector-style product illustration for an online shop catalogue.",
        "Single object, centred, front three-quarter view, occupying at most 75% of the",
        "frame height, with clear empty margin at the top and the bottom.",
        "Flat uniform background, very light warm grey (#FAF9F7). No transparency.",
        "Clean even lighting, minimal soft shadow, no gradients on the background.",
        "Limited warm palette: terracotta #D85A30, canvas #FAECE7, ink #2C2C2A,",
        "olive #5C7F38, plus the object's own colour."
    ];

    /// <summary>
    /// What the shop does not want to see, said where saying it works.
    ///
    /// The published template listed these as `No text, no logos, no labels...`
    /// inside the positive prompt, which is close to a no-op: CLIP does not
    /// negate, so "text" and "logo" enter the embedding like any other word. The
    /// mechanism that actually pushes away from a concept is classifier-free
    /// guidance against a second, opposite prompt — this one.
    /// </summary>
    public const string Negative =
        "text, letters, words, numbers, logo, watermark, label, signature, packaging, box, " +
        "hands, people, props, photograph, 3d render, gradient background, drop shadow, " +
        "border, frame, blurry, low quality";

    /// <summary>
    /// The prompt for one product. The subject is the English description, which
    /// names the object without the invented brand — a generator has never heard
    /// of a "Pulse Runner" and reads "cushioned running shoes" perfectly well.
    /// </summary>
    public static string Positive(SeedProduct product, ColourLexicon colours)
    {
        // Thirty-five products declare no colour, and one of them says why in
        // seed/IMAGES-TODO.md: the packing cubes are three cubes of three
        // colours, so the attribute was removed rather than a lie chosen. An
        // absent colour produces no clause at all, not an empty one.
        var colour = product.SpanishColour is { } spanish ? colours.English(spanish) : null;

        var subject = colour is null
            ? $"The object: {product.EnglishDescription}"
            : $"The object is {colour}: {product.EnglishDescription}";

        return string.Join(Environment.NewLine, [StyleLines[0], subject, .. StyleLines[1..]]);
    }
}
