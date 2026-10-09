namespace ElGuerre.Tendero.Knowledge.Features.ReadPictures;

/// <summary>
/// The closed questions a picture is asked: a question, and the schema its answer
/// is held to (phase 13a, I-2b and I-4).
///
/// **One file, compiled twice.** The shop asks these of a product's picture, and
/// `tools/SeedImages` asks them of the pictures it has drawn defects into
/// (`defects`), whose board may not fall. The tool links this file instead of
/// keeping a copy, so the wording that was measured is the wording that ships:
/// change a question here and the board measures the change. Constants only —
/// the tool references no project, and this gives it no type to depend on.
/// </summary>
internal static class PictureQuestions
{
    public const string CountSchema = """
        {
          "type": "object",
          "properties": { "count": { "type": "integer", "minimum": 0 } },
          "required": ["count"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// How many items are drawn — **a number, and nothing about whether it is the
    /// right one**. Asked whether a picture matched "a set of three", the model
    /// passed one pan; what it is asked here it can only read, and the comparison
    /// with what the catalogue holds is code's.
    /// </summary>
    public const string Count =
        "Look at this product illustration. "
        + "count: how many separate items are drawn? Count each whole item once, wherever it is in the picture, "
        + "and count the two shoes of a pair as two. Answer with the number only.";

    /// <summary>
    /// The colour of the product, to be chosen among the options the catalogue
    /// defines — which the closed question lists after it. Of forty seed
    /// pictures the shop's 4B chose the colour the catalogue holds for
    /// thirty-seven; whether the other three are wrong pictures is a person's to
    /// say, and that is what a doubt is for.
    /// </summary>
    public const string Colour = "Look at this product illustration. What is the main colour of the product itself?";
}
