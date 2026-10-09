using Microsoft.ML.Tokenizers;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// The text half of SigLIP 2's input (phase 13a, P13-1): a text as the sixty-four
/// token ids its text tower reads, so a picture can be searched for with words.
///
/// The model's tokenizer is Gemma's — SentencePiece over a 256,000-piece
/// vocabulary — and what the tower was trained on is: the text's pieces, the
/// end-of-sentence marker, and padding to sixty-four; a longer text cut to
/// sixty-three pieces and still ended. No beginning marker. A tokenizer that
/// gets any of that nearly right degrades retrieval without a sign, which is why
/// this class exists only behind a test that holds it, id for id, to what the
/// maker's own tokenizer produces at the pinned revision
/// (<c>SiglipTextTokenizerTests</c>, and the script that wrote its reference).
///
/// **It does not lower-case, and that is a question left open on purpose.** The
/// model's configuration says <c>do_lower_case: true</c>; the maker's tokenizer
/// at this revision does not do it — <c>ZAPATILLAS</c> and <c>zapatillas</c> get
/// different ids — and this class is faithful to the tokenizer, not to the
/// flag. Whether a text should be lower-cased before it reaches here is for
/// whoever embeds it to decide, by measuring retrieval both ways.
/// </summary>
public sealed class SiglipTextTokenizer
{
    public const string Model = "google/siglip2-base-patch16-224";

    /// <summary>The maker's commit the vocabulary, and the reference it is tested against, are of.</summary>
    public const string Revision = "75de2d55ec2d0b4efc50b3e9ad70dba96a7b2fa2";

    /// <summary>The window the text tower was trained with: padded to it, cut at it.</summary>
    public const int Length = 64;

    /// <summary>The vocabulary's file, as the maker's repository names it.</summary>
    public const string FileName = "tokenizer.model";

    /// <summary>The piece the vocabulary pads with, by the name the maker gives it.</summary>
    private const string Padding = "<pad>";

    private readonly SentencePieceTokenizer pieces;
    private readonly int padding;

    private SiglipTextTokenizer(SentencePieceTokenizer pieces, int padding) => (this.pieces, this.padding) = (pieces, padding);

    public static SiglipTextTokenizer Load(string path)
    {
        using var model = File.OpenRead(path);
        // Neither marker added by the library: the end is this class's to place, after the cut.
        var pieces = SentencePieceTokenizer.Create(model, false, false);

        // Read from the vocabulary, not assumed: a file that pads with another id is another model's.
        return pieces.Vocabulary.TryGetValue(Padding, out var padding)
            ? new SiglipTextTokenizer(pieces, padding)
            : throw new InvalidDataException($"'{path}' has no {Padding} piece: it is not SigLIP 2's vocabulary.");
    }

    /// <summary>The text's ids: its pieces, cut to leave room for the end marker, the marker, and padding to <see cref="Length"/>.</summary>
    public long[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var ids = new long[Length];
        Array.Fill(ids, this.padding);

        var at = 0;
        foreach (var id in this.pieces.EncodeToIds(text, addBeginningOfSentence: false, addEndOfSentence: false).Take(Length - 1))
            ids[at++] = id;
        ids[at] = this.pieces.EndOfSentenceId;

        return ids;
    }
}
