using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// CLIP's byte-level BPE tokenizer, which SDXL runs twice — once per text tower.
///
/// It is written out rather than taken from a package because the whole tool is
/// two dependencies by decision, and because the failure mode of a *nearly*
/// correct tokenizer is not an exception: it is ids that are plausible, a prompt
/// the model reads as something else, and an image that looks like the model is
/// bad. Every step below has a test whose oracle is the vocabulary file itself.
///
/// The three places these go wrong, all covered:
///
/// 1. **The end-of-word marker** is glued to the last character of each word
///    before merging, so "olive" alone and "olive" inside a sentence are the same
///    token.
/// 2. **Digits do not group.** The pre-tokenizer takes one digit at a time, which
///    is why a hex colour costs five or six tokens and why the published prompt
///    template ran out of its 77-token budget.
/// 3. **The two tokenizers pad with different tokens** — the end marker in the
///    first, `!` in the second — so the pad id is read from
///    `special_tokens_map.json` and never assumed.
/// </summary>
public sealed partial class ClipTokenizer
{
    /// <summary>CLIP's context window. Everything past it is discarded, silently.</summary>
    public const int ContextLength = 77;

    private const string StartMarker = "<|startoftext|>";
    private const string EndMarker = "<|endoftext|>";
    private const string WordEnd = "</w>";

    private readonly Dictionary<string, int> _vocab;
    private readonly Dictionary<(string First, string Second), int> _ranks;
    private readonly Dictionary<byte, char> _byteToChar;
    private readonly Dictionary<string, string[]> _merged = [];

    /// <summary>The token this tokenizer fills the rest of the window with.</summary>
    public int PadId { get; }

    private ClipTokenizer(
        Dictionary<string, int> vocab,
        Dictionary<(string, string), int> ranks,
        Dictionary<byte, char> byteToChar,
        int padId)
    {
        _vocab = vocab;
        _ranks = ranks;
        _byteToChar = byteToChar;
        PadId = padId;
    }

    public static ClipTokenizer Load(string directory)
    {
        var vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(
            File.ReadAllText(Path.Combine(directory, "vocab.json")))
            ?? throw new InvalidDataException($"No vocabulary in '{directory}'.");

        var ranks = new Dictionary<(string, string), int>();
        var rank = 0;

        foreach (var line in File.ReadLines(Path.Combine(directory, "merges.txt")))
        {
            // The file opens with a "#version: 0.2" header that is not a merge.
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var parts = line.Split(' ');

            if (parts.Length == 2)
                ranks[(parts[0], parts[1])] = rank++;
        }

        return new ClipTokenizer(vocab, ranks, ByteToChar(), PadIdFrom(directory, vocab));
    }

    /// <summary>The id the vocabulary gives a token, so a test can use the
    /// artefact as its own oracle instead of a number typed twice.</summary>
    public int IdOf(string token) => _vocab[token];

    /// <summary>The content ids, with no markers and no padding.</summary>
    public int[] Encode(string text)
    {
        List<int> ids = [];

        foreach (Match match in Pattern().Matches(Clean(text)))
        {
            // Every byte of the UTF-8 form becomes a printable character, so a
            // byte sequence that is not valid text still has a token.
            var mapped = string.Concat(Encoding.UTF8.GetBytes(match.Value).Select(b => _byteToChar[b]));

            foreach (var piece in Merge(mapped))
                if (_vocab.TryGetValue(piece, out var id))
                    ids.Add(id);
        }

        return [.. ids];
    }

    /// <summary>How many tokens a prompt costs, which is the number that decides
    /// whether it survives the window.</summary>
    public int CountContentTokens(string text) => Encode(text).Length;

    /// <summary>
    /// The window as the encoder wants it: the start marker, the content, the end
    /// marker, and padding. A prompt too long is cut, and the end marker is put
    /// back afterwards — the encoder reads that position, so a truncation that
    /// loses it is worse than the truncation itself.
    /// </summary>
    public int[] EncodeToLength(string text, int length = ContextLength)
    {
        var content = Encode(text);
        var room = length - 2;

        if (content.Length > room)
            content = content[..room];

        var ids = new int[length];
        Array.Fill(ids, PadId);

        ids[0] = _vocab[StartMarker];
        content.CopyTo(ids, 1);
        ids[content.Length + 1] = _vocab[EndMarker];

        return ids;
    }

    /// <summary>
    /// How many 77-token windows a prompt needs.
    ///
    /// **This is what removes the budget rather than managing it.** CLIP's window
    /// is 77 and cross-attention does not care how long the sequence is: the
    /// UNet declares `encoder_hidden_states` as `[-1, -1, 2048]`, so two windows
    /// encoded separately and laid end to end are a longer conditioning, not an
    /// error. It is what every serious interface to these models does, and it
    /// costs one extra pass through a 123M-parameter encoder.
    /// </summary>
    public int WindowsNeeded(string text) =>
        Math.Max(1, (int)Math.Ceiling(Encode(text).Length / (double)(ContextLength - 2)));

    /// <summary>
    /// The prompt cut into whole windows, each wrapped in its own markers and
    /// padded. Asking for more windows than the text needs pads with empty ones,
    /// which is how the positive and the negative are made the same length —
    /// both rows of the guidance batch have to be.
    /// </summary>
    public int[][] EncodeWindows(string text, int windows)
    {
        var content = Encode(text);
        var room = ContextLength - 2;

        var result = new int[windows][];

        for (var window = 0; window < windows; window++)
        {
            var from = window * room;
            var slice = from >= content.Length
                ? []
                : content[from..Math.Min(from + room, content.Length)];

            var ids = new int[ContextLength];
            Array.Fill(ids, PadId);

            ids[0] = _vocab[StartMarker];
            slice.CopyTo(ids, 1);
            ids[slice.Length + 1] = _vocab[EndMarker];

            result[window] = ids;
        }

        return result;
    }

    /// <summary>
    /// Lowercase, and one space between words. CLIP's reference cleaner also runs
    /// `ftfy` over the text to repair mojibake; for English product prose written
    /// in this repository there is nothing for it to repair, and pulling a
    /// dependency to do nothing is the opposite of the point.
    /// </summary>
    private static string Clean(string text) =>
        Whitespace().Replace(text, " ").Trim().ToLowerInvariant();

    /// <summary>
    /// Greedy byte-pair merging: repeatedly join the adjacent pair with the
    /// lowest rank until no pair in the word is known.
    /// </summary>
    private string[] Merge(string mapped)
    {
        if (_merged.TryGetValue(mapped, out var cached))
            return cached;

        if (mapped.Length == 0)
            return [];

        // The end-of-word marker rides on the LAST character, which is what makes
        // a whole word one token and the same letters as a prefix a different one.
        List<string> word = [.. mapped[..^1].Select(character => character.ToString())];
        word.Add(mapped[^1] + WordEnd);

        while (word.Count > 1)
        {
            var best = -1;
            var bestRank = int.MaxValue;

            for (var index = 0; index < word.Count - 1; index++)
                if (_ranks.TryGetValue((word[index], word[index + 1]), out var candidate) && candidate < bestRank)
                {
                    bestRank = candidate;
                    best = index;
                }

            if (best < 0)
                break;

            var joined = word[best] + word[best + 1];
            word.RemoveRange(best, 2);
            word.Insert(best, joined);
        }

        return _merged[mapped] = [.. word];
    }

    /// <summary>
    /// GPT-2's byte-to-character table, which CLIP inherits: the printable bytes
    /// map to themselves and the rest are lifted into an unused block, so every
    /// byte has a character and none of them is whitespace.
    /// </summary>
    private static Dictionary<byte, char> ByteToChar()
    {
        List<int> printable =
        [
            .. Enumerable.Range('!', '~' - '!' + 1),
            .. Enumerable.Range('¡', '¬' - '¡' + 1),
            .. Enumerable.Range('®', 'ÿ' - '®' + 1)
        ];

        var table = new Dictionary<byte, char>();
        var lifted = 0;

        for (var value = 0; value < 256; value++)
            table[(byte)value] = printable.Contains(value)
                ? (char)value
                : (char)(256 + lifted++);

        return table;
    }

    /// <summary>
    /// The pad token, read and not assumed. `tokenizer` pads with the end marker
    /// and `tokenizer_2` pads with `!`, and the field is either a plain string or
    /// an object carrying a `content` — both shapes appear in the wild.
    /// </summary>
    private static int PadIdFrom(string directory, Dictionary<string, int> vocab)
    {
        var path = Path.Combine(directory, "special_tokens_map.json");

        if (!File.Exists(path))
            return vocab[EndMarker];

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty("pad_token", out var pad))
            return vocab[EndMarker];

        var token = pad.ValueKind == JsonValueKind.Object
            ? pad.GetProperty("content").GetString()
            : pad.GetString();

        return token is not null && vocab.TryGetValue(token, out var id) ? id : vocab[EndMarker];
    }

    /// <summary>
    /// CLIP's pre-tokenizer. The clause that matters here is the one for numbers:
    /// **one digit at a time**, which is why hex codes are expensive.
    /// </summary>
    [GeneratedRegex(
        @"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+",
        RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
