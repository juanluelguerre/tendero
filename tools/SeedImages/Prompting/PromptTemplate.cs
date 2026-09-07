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
    /// The register, minus the subject.
    ///
    /// **Ordered by what can least afford to be cut.** The subject goes first, so
    /// what truncation eats is the tail of this block — and the tail is therefore
    /// the composition line, which `SafeAreaProbe` checks independently by
    /// counting pixels. The background and the lighting come before it, because
    /// they are what makes a hundred separate images look like one catalogue and
    /// nothing else checks them. Published verbatim in
    /// <c>seed/IMAGES-TODO.md</c>, and a test asserts every line is still there —
    /// the document is what a person reads to know what the hundred images are
    /// supposed to look like, and code and prose drifting means the prose wins
    /// while being wrong.
    /// </summary>
    public static readonly string[] StyleLines =
    [
        "Flat vector illustration of one product, isolated and centred.",
        "Plain light warm grey background, even lighting, soft shadow.",
        "Three-quarter view, zoomed out, the whole object small within the frame,",
        "occupying at most 70% of the height, with wide empty margins above and below."
    ];

    /// <summary>
    /// What the shop does not want to see, said where saying it works.
    ///
    /// The published template listed these as `No text, no logos, no labels...`
    /// inside the positive prompt, which is close to a no-op: CLIP does not
    /// negate, so "text" and "logo" enter the embedding like any other word. The
    /// mechanism that actually pushes away from a concept is classifier-free
    /// guidance against a second, opposite prompt — this one.
    ///
    /// **The first block of it was bought with the first image.** Trimming the
    /// style block to fit 75 tokens took out "occupying at most 75% of the frame
    /// height, with clear empty margin at the top and the bottom", and the model
    /// answered with a studio sheet: eight views of the same backpack, filling
    /// the frame corner to corner. Asking for one object belongs here rather than
    /// in the positive prompt, and it costs nothing — this window is its own, and
    /// was only half used.
    /// </summary>
    public const string Negative =
        "multiple objects, several views, product sheet, contact sheet, collage, grid, " +
        "tiled, repeated, duplicated, variations, " +
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

        // **The name carries the noun and the description does not.** The travel
        // backpack's description begins "Front-loading pack with stowable
        // straps, sized to go in the cabin", and the model drew a cabin suitcase
        // -- correctly, from what it was told. `IMAGES-TODO.md` recommended the
        // description alone because it avoids the invented brand; the brand is a
        // token of noise a diffusion model ignores, and the noun is the whole
        // subject.
        var what = $"{product.EnglishName}. {product.EnglishDescription}";

        var subject = colour is null
            ? $"The object: {what}"
            : $"The object is {colour}: {what}";

        return string.Join(Environment.NewLine, [StyleLines[0], subject, .. StyleLines[1..]]);
    }
}
