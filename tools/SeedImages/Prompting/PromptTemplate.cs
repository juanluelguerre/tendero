namespace ElGuerre.Tendero.SeedImages;

/// <summary>Another view of a product that already has a picture.</summary>
public enum PictureView
{
    /// <summary>From behind.</summary>
    Back,

    /// <summary>From the side.</summary>
    Side,
}

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
        OneObject,
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
    /// <summary>
    /// What a picture is told to become when it is another colour of the same
    /// product (phase 13a, I-4d). **Everything it must keep is named, because
    /// what is not named gets redrawn**: the object, its lines, its place and
    /// size in the frame, the background. And the one thing a dark colour needs
    /// said — the first black pannier came back as a flat silhouette — is that
    /// its seams and parts stay drawn, in a lighter line.
    /// </summary>
    /// <param name="from">The colour the reference is, in English, or null when the catalogue does not say.</param>
    /// <param name="to">The colour it is to be, in English.</param>
    public static string Recolour(string? from, string to, string? hint = null) =>
        $"""
         This is a product illustration from a shop's catalogue. Give back the same
         illustration with one change: the product's main colour{(from is null ? string.Empty : $", which is {from},")} becomes {to}.

         Keep everything else exactly as it is: the same object, the same shape and
         proportions, the same outlines, seams, pockets, straps, buckles, handles
         and hooks in the same places, the same viewing angle, the same size and
         position in the frame, the same plain background and the same soft shadow.
         Do not redraw the object, do not add or remove any part, and do not add
         any text, logo, label or second object.

         Only the surfaces that are {from ?? "the main colour"} change. Parts that are another
         colour — dark straps, buckles, hooks, zips — keep their own colour, unless
         they would no longer be told apart from the new colour: then make them a
         mid grey. Keep every outline and seam visible: on a dark object draw them
         in a lighter grey, so that it still reads as a detailed illustration and
         never as a flat silhouette.{(string.IsNullOrWhiteSpace(hint) ? string.Empty : " " + hint.Trim())}
         """;

    /// <summary>
    /// What a picture is told to become when it is another view of the same
    /// product (phase 13a, I-4d): the same object, turned. **This is a redraw,
    /// and says so** — a recolour moves nothing and can be held to the original
    /// pixel by pixel; a view from behind is a different shape on purpose, and
    /// what it is held to is a person's eye. So everything the object is — its
    /// colours, its materials, its parts, the way it is drawn — is named as what
    /// stays, and the angle as the one thing that changes.
    /// </summary>
    public static string View(PictureView view, string? hint = null) =>
        $"""
         This is a product illustration from a shop's catalogue. Draw the same
         product again, {Angle(view)}.

         It is the same object: the same colours, the same materials, the same
         proportions, and every part it has — seams, pockets, straps, buckles,
         handles, hooks — where it would be from this side. Do not invent a part
         the object does not have, and do not leave out one it has.

         Draw it the same way: a flat illustration with clean outlines and flat
         colours, the same plain light background with a soft shadow, the object
         alone, centred, the same size in the frame, with wide empty margins. No
         text, logo, label or second object anywhere.{(string.IsNullOrWhiteSpace(hint) ? string.Empty : " " + hint.Trim())}
         """;

    private static string Angle(PictureView view) => view switch
    {
        PictureView.Back => "seen from behind: the side that faces away in this picture",
        PictureView.Side => "seen from the side: turned a quarter turn, so its side faces the viewer",
        _ => throw new ArgumentOutOfRangeException(nameof(view)),
    };

    /// <param name="hint">For a redraw: what was wrong with the last drawing, said as what the object is. It follows the description.</param>
    public static string Positive(SeedProduct product, ColourLexicon colours, string? hint = null)
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
        var description = WithoutCareInstructions(WithoutMeasurements(product.EnglishDescription));

        if (!string.IsNullOrWhiteSpace(hint))
            description = $"{description} {hint.Trim()}";

        var subject = colour is null
            ? $"The object: {description}"
            : $"The object is {colour}: {description}";

        // A set is not "one single object", and saying both got four knives for
        // three and seven tumblers for six.
        var composition = product.Pieces is > 1 and var pieces
            ? StyleLines[1..].Select(line => line == OneObject ? SetOf(pieces) : line)
            : StyleLines[1..];

        return string.Join(Environment.NewLine, [StyleLines[0], subject, .. composition]);
    }

    /// <summary>The line a set replaces: it asks for one object, and a set is several.</summary>
    private const string OneObject = "One single object, isolated and centred, three-quarter view,";

    private static readonly string[] Numbers =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    /// <summary>
    /// How many to draw, for a product that declares its pieces. **In a word, not
    /// a figure**: a figure in a prompt is what gets lettered onto the product.
    /// "Each one whole" because the generator stacks and overlaps a set, and an
    /// overlapped set is one nobody can count — the model that checks it least of all.
    /// </summary>
    public static string SetOf(int pieces) =>
        $"Exactly {(pieces < Numbers.Length ? Numbers[pieces] : pieces.ToString(System.Globalization.CultureInfo.InvariantCulture))} of them, "
        + "each one whole and none hidden behind another, grouped and centred, three-quarter view,";

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
    /// The description without the sentences that say how the thing is washed.
    ///
    /// **Bought with two identical failures.** A house slipper described as "Hand
    /// washable." came back twice with a round emblem on its side: the hand-wash
    /// symbol, as a care label carries it. The same lesson as the measurements, one
    /// kind of sentence over: what a draughtsman cannot draw as the object, the model
    /// draws as something printed on it.
    ///
    /// Narrow on purpose. Fifteen sentences of the catalogue mention washing, an
    /// iron or a dishwasher, and fourteen of them are the product itself — a wash
    /// bag, washed linen, a cast iron pan. Only an instruction of the kind a care
    /// label carries goes.
    /// </summary>
    public static string WithoutCareInstructions(string description)
    {
        var sentences = description
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(sentence => !CareInstruction.IsMatch(sentence));

        return String.Join(". ", sentences) + ".";
    }

    private static readonly System.Text.RegularExpressions.Regex CareInstruction = new(
        @"\b(?:hand|machine)[\s-]wash(?:able)?\b|\bdry[\s-]clean",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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
        + @"hour|hours|cup|cups|litre|litres|liter|liters|inch|inches|"
        // An angle, with what it measures: "350 degrees of rotation" leaves
        // nothing behind, where "350 degrees" alone would leave "of rotation".
        + @"degrees?(?:\s+of\s+\w+)?)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex BeforePunctuation =
        new(@"\s+([,.;:])");

    private static readonly System.Text.RegularExpressions.Regex Gap = new(@"\s{2,}");
}
