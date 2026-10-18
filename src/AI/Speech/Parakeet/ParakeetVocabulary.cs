using System.Text;

namespace ElGuerre.Tendero.AI.Speech.Parakeet;

/// <summary>
/// Parakeet's SentencePiece pieces, by id, and the words they spell: "▁" starts a
/// word, and a piece written in angle brackets — the blank, <c>&lt;unk&gt;</c>,
/// <c>&lt;|nospeech|&gt;</c> — is never written.
/// </summary>
public sealed class ParakeetVocabulary(IReadOnlyList<string> pieces)
{
    private const char WordMark = '▁';

    public int Count => pieces.Count;

    public string Words(IEnumerable<int> tokens)
    {
        var text = new StringBuilder();
        foreach (var token in tokens)
        {
            var piece = pieces[token];
            if (piece.StartsWith('<') && piece.EndsWith('>'))
                continue;
            text.Append(piece.Replace(WordMark, ' '));
        }

        return text.ToString().Trim();
    }

    /// <summary>The export's <c>vocab.json</c>: a JSON array of the pieces, by id.</summary>
    public static ParakeetVocabulary FromFile(string path) =>
        new(System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{path} holds no pieces."));
}
