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
        "Flat vector-style product illustration.",
        "Bold clean outlines, solid saturated colours, flat shading.",
        "Plain unmarked surfaces with no writing, no labels and no badges.",
        "Plain light warm grey background, even lighting, minimal soft shadow.",
        "One single object, isolated and centred, three-quarter view,",
        "at most 70% of the frame height, with wide empty margins above and below."
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
    /// **Two of its blocks were bought with images.** The first came from a
    /// studio sheet; the second from a batch of five in which the desk lamp
    /// arrived standing on a wooden desk beside a laptop and two notebooks. The
    /// prompt already said "props" and "photograph", and a scene is neither — it
    /// is a room, so the room had to be named.
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
        "text, letters, words, numbers, writing, lettering, typography, caption, " +
        "logo, brand mark, badge, tag, label, sticker, watermark, signature, " +
        "photographic, photorealistic, texture, grain, noise, soft focus, depth of field, " +
        "washed out, desaturated, pastel, muted, " +
        "border, outer frame, panel, box around the image, vignette, " +
        "bicycle, wheel, vehicle, mounted on something, attached to something, " +
        "multiple objects, several views, product sheet, contact sheet, collage, grid, " +
        "tiled, repeated, duplicated, variations, " +
        "desk, table, furniture, room, interior, scene, floor, wall, shelf, " +
        "packaging, box, hands, people, props, 3d render, gradient background, drop shadow, " +
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

        // **The description alone, and the name deliberately left out.**
        //
        // Putting the name in was tried and cost more than it bought. It fixed
        // one product -- a travel backpack whose description reads "sized to go
        // in the cabin", which the model drew as cabin luggage -- and broke
        // another: "20L waterproof bike pannier" made it draw a whole bicycle
        // with a pannier on it, where the description alone gives the pannier.
        //
        // A name carries the brand, which is noise, and often carries the
        // product's CONTEXT as a modifier, which is worse than noise: "bike"
        // modifies "pannier" for a reader and is a second object for a diffusion
        // model. The eight illustrations made by hand used the description alone
        // and none of them drew the wrong thing.
        var description = WithoutMeasurements(product.EnglishDescription);

        var subject = colour is null
            ? $"The object: {description}"
            : $"The object is {colour}: {description}";

        return string.Join(Environment.NewLine, [StyleLines[0], subject, .. StyleLines[1..]]);
    }

    /// <summary>
    /// The description with its figures taken out.
    ///
    /// **Bought with three identical failures.** A trail shoe described as having
    /// "4 mm lugs" came back three times with `4 mm` lettered onto its midsole,
    /// and a coffee maker with a "24-hour timer" came back with `24h` on its
    /// display — in both cases after the instruction had been told, in plain
    /// words, not to write numbers on the product. It kept doing it because the
    /// figure was in the sentence it was drawing from, and a measurement beside a
    /// part reads as that part's label.
    ///
    /// Asking harder was the wrong repair. **A measurement tells a draughtsman
    /// nothing** — 4 mm of lug is a moulded sole at any scale, 250 g of merino is
    /// a jumper — so the figures were never earning their place in a prompt, and
    /// removing them is deterministic where the instruction was a plea. It is
    /// CLAUDE.md's own rule about what a model decides and what code decides,
    /// applied one layer further out than usual.
    ///
    /// Counts survive on purpose: "Set of 3 frying pans" carries no unit, and the
    /// three pans are the product.
    /// </summary>
    public static string WithoutMeasurements(string description)
    {
        var trimmed = Measurement.Replace(description, string.Empty);

        // The removal leaves the join behind -- "4 mm lugs" becomes " lugs" and
        // "12 cups, a timer" becomes " , a timer" -- so the punctuation is closed
        // up rather than published with a hole in it.
        var closed = Gap.Replace(BeforePunctuation.Replace(trimmed, "$1"), " ").Trim();

        // And "8 mm drop." becomes the sentence "drop.", which is not a fact
        // about anything. A sentence that is down to one word was a
        // measurement's sentence, so it goes with it.
        var sentences = closed
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(sentence => sentence.Contains(' '));

        return String.Join(". ", sentences) + ".";
    }

    /// <summary>
    /// A figure with a unit attached: `4 mm`, `12 cups`, `24-hour`, `UPF 30`.
    ///
    /// **A run of figures sharing one unit is one measurement**, because only the
    /// last of them carries it: `20, 24 and 28 cm pans` matched as three separate
    /// things leaves `20, 24 and pans`, which is worse than what it replaced.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex Measurement = new(
        @"\b(?:UPF|SPF|IPX)\s*\d+\b"
        + @"|\b\d+(?:[.,]\d+)?(?:\s*(?:,|and|y)\s*\d+(?:[.,]\d+)?)*"
        + @"\s*-?\s*(?:mm|cm|m|g|kg|ml|l|w|v|"
        + @"hour|hours|cup|cups|litre|litres|liter|liters|inch|inches)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex BeforePunctuation =
        new(@"\s+([,.;:])");

    private static readonly System.Text.RegularExpressions.Regex Gap = new(@"\s{2,}");
}
