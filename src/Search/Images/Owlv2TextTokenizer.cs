using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.Tokenizers;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// The words a detector is asked for, as the sixteen token ids OWLv2's text tower
/// reads (the camera search, CS-2). The tokenizer is CLIP's: lower-cased, runs of
/// white space made one, split into words, numbers and punctuation, and each word
/// cut by byte-level BPE with an end-of-word mark; then the start marker, at most
/// fourteen pieces, the end marker, and padding to sixteen.
///
/// Like <see cref="SiglipTextTokenizer"/>, it exists only behind a test that holds
/// it id for id to the maker's tokenizer at the pinned revision
/// (<c>Owlv2TextTokenizerTests</c>, written by <c>prepare-owlv2.py</c>).
/// </summary>
public sealed partial class Owlv2TextTokenizer
{
    /// <summary>The window the text tower was trained with.</summary>
    public const int Length = 16;

    public const string VocabularyFile = "vocab.json";
    public const string MergesFile = "merges.txt";

    private const string Start = "<|startoftext|>";
    private const string End = "<|endoftext|>";

    /// <summary>The token the maker's processor pads with, by its name (the export asserts it).</summary>
    private const string Padding = "!";

    private readonly BpeTokenizer pieces;
    private readonly (int Start, int End, int Padding) marks;

    private Owlv2TextTokenizer(BpeTokenizer pieces, (int, int, int) marks) => (this.pieces, this.marks) = (pieces, marks);

    /// <summary>CLIP's split: contractions, runs of letters, single digits, runs of anything else; white space between them is dropped.</summary>
    [GeneratedRegex(@"'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+")]
    private static partial Regex Words();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <param name="folder">Where the export left the maker's <see cref="VocabularyFile"/> and <see cref="MergesFile"/>.</param>
    public static Owlv2TextTokenizer Load(string folder)
    {
        using var vocabulary = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, VocabularyFile)));
        int Id(string token) => vocabulary.RootElement.TryGetProperty(token, out var id)
            ? id.GetInt32()
            : throw new InvalidDataException($"'{folder}' has no {token} token: it is not CLIP's vocabulary.");
        var marks = (Start: Id(Start), End: Id(End), Padding: Id(Padding));

        // The padding token is one of the maker's added tokens, so its tokenizer
        // matches every "!" in a text as that token, before any word is cut: "kettle!"
        // ends in the padding's id, not in "!</w>". Faithful to the maker's, as the
        // reference holds; no kind's words have one.
        Dictionary<string, int> special = new() { [Start] = marks.Start, [End] = marks.End, [Padding] = marks.Padding };
        var pieces = BpeTokenizer.Create(new BpeOptions(Path.Combine(folder, VocabularyFile), Path.Combine(folder, MergesFile))
        {
            PreTokenizer = new RegexPreTokenizer(Words(), special),
            SpecialTokens = special,
            EndOfWordSuffix = "</w>",
            ByteLevel = true,
        });

        return new Owlv2TextTokenizer(pieces, marks);
    }

    /// <summary>The text's ids: the start marker, its pieces cut to leave room for the end marker, the marker, and padding to <see cref="Length"/>.</summary>
    public long[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var ids = new long[Length];
        Array.Fill(ids, this.marks.Padding);

        var at = 0;
        ids[at++] = this.marks.Start;
        foreach (var id in this.pieces.EncodeToIds(Spaces().Replace(text, " ").Trim().ToLowerInvariant()).Take(Length - 2))
            ids[at++] = id;
        ids[at] = this.marks.End;

        return ids;
    }
}
