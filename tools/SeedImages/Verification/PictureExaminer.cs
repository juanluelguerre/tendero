namespace ElGuerre.Tendero.SeedImages;

/// <summary>What a picture is asked. Each one is measured on its own against the known-bad set (`defects`).</summary>
public enum Question
{
    /// <summary>A letter, a word, a number, a logo, a watermark.</summary>
    Text,

    /// <summary>Something printed on the product that is not the product: a symbol, an emblem, a care label.</summary>
    Mark,

    /// <summary>Anything drawn besides the product.</summary>
    Scene,

    /// <summary>The colour the model reads against the one the catalogue declares.</summary>
    Colour,

    /// <summary>How many the model counts against the pieces the catalogue declares.</summary>
    Count,

    /// <summary>Whether the model says the picture is the product described.</summary>
    Description,

    /// <summary>Whether it is drawn as the reference picture is.</summary>
    Register,
}

/// <summary>One question's doubt about a picture, in words a person reads beside it.</summary>
public sealed record Doubt(Question Question, string Says);

/// <summary>Everything the questions said about one picture.</summary>
/// <param name="Colour">The colour the model read, which the report prints for a picture nothing is wrong with.</param>
public sealed record Examination(string Colour, IReadOnlyList<Doubt> Doubts)
{
    public bool Raises(Question question) => Doubts.Any(doubt => doubt.Question == question);
}

/// <summary>
/// Every question, asked of one picture, and what each answer means for it —
/// in one place, so `verify` and `defects` cannot come to judge differently.
///
/// **The model reads; code compares** (invariant 12; phase 13a, I-2b). Where the
/// catalogue holds a value — a colour, a number of pieces — the model is asked
/// for the value it sees and never whether the two agree: asked that, it passed
/// one pan as a set of three. The open question about the description stays, as
/// one more voice.
///
/// **A doubt is a proposal.** It marks a picture for a person to look at.
/// </summary>
public sealed class PictureExaminer(VisionChecks vision, ColourLexicon colours)
{
    /// <summary>What every picture of the catalogue has besides its product.</summary>
    private static readonly string[] NotAScene = ["shadow", "background"];

    /// <param name="product">The product the picture is of, or null for a file the catalogue does not know: it is then asked only what needs no product.</param>
    /// <param name="reference">The picture whose register every other is held to, or null when this one is it.</param>
    /// <param name="referenceName">That picture's name, for the doubt's wording.</param>
    public Examination Examine(byte[] picture, SeedProduct? product, byte[]? reference, string referenceName)
    {
        var look = vision.Look(picture, colours.EnglishLabels);
        List<Doubt> doubts = [];

        if (look.HasText)
            doubts.Add(new(Question.Text, "there is text or a symbol in it"));

        if (vision.Marked(picture))
            doubts.Add(new(Question.Mark, "something is printed on the product: a symbol, an emblem or a label"));

        // The model lists what else it sees and code says whether that is a scene:
        // every picture has the product's own shadow and a background, and the
        // model lists both as things drawn.
        var others = vision.Others(picture)
            .Where(other => !NotAScene.Any(word => other.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (others.Length > 0)
            doubts.Add(new(Question.Scene, $"something else is drawn beside the product: {string.Join(", ", others)}"));
        else if (look.HasScene)
            doubts.Add(new(Question.Scene, "something else is drawn beside the product"));

        // **The colour is asked by itself and in plain text** (phase 13a, I-4d),
        // not read from the look's schema, where it travelled with two other
        // answers: there the model called a terracotta pannier black. An answer
        // that is none of the catalogue's colours is nothing to hold against the
        // one declared.
        var colour = vision.ColourOf(picture, colours.EnglishLabels);

        if (product?.SpanishColour is { } spanish
            && colours.English(spanish) is { } declared
            && colour is not null
            && !string.Equals(declared, colour, StringComparison.OrdinalIgnoreCase))
        {
            doubts.Add(new(Question.Colour, $"catalogue says {declared}, the picture looks {colour}"));
        }

        // Asked only of a product that says how many it is: a pair of shoes
        // declares nothing, and there is no number to hold the answer to.
        if (product?.Pieces is { } pieces && vision.Count(picture) is var seen && seen != pieces)
            doubts.Add(new(Question.Count, $"{seen} drawn, the catalogue says {pieces}"));

        if (product is not null && vision.Against(picture, product.EnglishName, product.EnglishDescription) is { Matches: false } against)
            doubts.Add(new(Question.Description, $"it shows \"{against.Shows}\", not the product described: {against.Differs}"));

        if (reference is not null && vision.Beside(picture, reference) is { } beside && !(beside.SameStyle && beside.SameBackground))
            doubts.Add(new(Question.Register, $"it is not drawn as {referenceName} is: {beside.Differs}"));

        return new Examination(colour ?? "none of the catalogue's", doubts);
    }
}
