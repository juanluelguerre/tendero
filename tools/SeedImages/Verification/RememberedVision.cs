using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// A vision model that is not asked twice (phase 13a).
///
/// The defects board asks some forty questions of every picture of the catalogue
/// — as it is, and with each kind of defect drawn into it — and over 123
/// pictures that is eighty-five minutes of a GPU. Almost all of it is asking
/// again what was asked the last time: the model answers at temperature zero
/// with a fixed seed, so the same question about the same picture is the same
/// answer. This keeps the answers, and a run pays only for what is new.
///
/// **An answer is remembered by everything that was sent to get it**: the
/// model's name, the kind of question, its words, its schema and the bytes of
/// every picture, in order. So nothing has to be told that something changed —
/// a reworded question, a redrawn picture, a defect drawn differently are other
/// questions, and are asked. What this cannot see is the weights changing under
/// the same name (another quantisation, another projector): that is what
/// <c>--fresh</c> is for.
///
/// **A run whose every question is remembered needs no model at all**, so the
/// model is reached for only when the first question nothing remembers comes.
/// </summary>
public sealed class RememberedVision(IVisionModel inner, IReadOnlyDictionary<string, string> remembered) : IVisionModel
{
    private readonly Dictionary<string, string> used = new(StringComparer.Ordinal);
    private bool reached;

    public string Name => inner.Name;

    /// <summary>How many questions went to the model in this run.</summary>
    public int Asked { get; private set; }

    /// <summary>How many were answered from what it had answered before, repeats within the run included.</summary>
    public int Remembered { get; private set; }

    /// <summary>
    /// The answers this run used, old and new, by their key: what is worth
    /// keeping. An answer about a picture that is gone, or to a question since
    /// reworded, is not in it — the memory does not grow for ever.
    /// </summary>
    public IReadOnlyDictionary<string, string> Used => this.used;

    /// <summary>Nothing yet: whether a model is needed at all is not known until a question nothing remembers.</summary>
    public void EnsureReachable()
    {
    }

    public string Ask(IReadOnlyList<byte[]> pngs, string question, string schema) =>
        Recall("ask", [question, schema], pngs, () => inner.Ask(pngs, question, schema));

    public string Describe(byte[] png) => Recall("describe", [], [png], () => inner.Describe(png));

    public string Answer(byte[] png, string question) => Recall("answer", [question], [png], () => inner.Answer(png, question));

    private string Recall(string kind, IReadOnlyList<string> words, IReadOnlyList<byte[]> pictures, Func<string> ask)
    {
        var key = KeyOf(inner.Name, kind, words, pictures);

        if (this.used.TryGetValue(key, out var answer) || remembered.TryGetValue(key, out answer))
        {
            Remembered++;
            this.used[key] = answer;
            return answer;
        }

        if (!this.reached)
        {
            inner.EnsureReachable();
            this.reached = true;
        }

        Asked++;
        return this.used[key] = ask();
    }

    /// <summary>
    /// One hash over every part, each with its length in front of it: where the
    /// question ends and its schema begins, and where one picture ends and the
    /// next begins, is part of what was asked.
    /// </summary>
    private static string KeyOf(string model, string kind, IReadOnlyList<string> words, IReadOnlyList<byte[]> pictures)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Part(hash, Encoding.UTF8.GetBytes(model));
        Part(hash, Encoding.UTF8.GetBytes(kind));

        Count(hash, words.Count);
        foreach (var word in words)
            Part(hash, Encoding.UTF8.GetBytes(word));

        Count(hash, pictures.Count);
        foreach (var picture in pictures)
            Part(hash, picture);

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Part(IncrementalHash hash, ReadOnlySpan<byte> part)
    {
        Count(hash, part.Length);
        hash.AppendData(part);
    }

    private static void Count(IncrementalHash hash, int count)
    {
        Span<byte> written = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(written, count);
        hash.AppendData(written);
    }
}

/// <summary>
/// Where the answers are kept between runs: one answer a line, ordered by key,
/// so the same answers are the same file and a run that asked three new things
/// is a diff of three lines.
/// </summary>
public static class AnswerMemory
{
    private sealed record Line(string Key, string Answer);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>What was kept; nothing, when nothing ever was.</summary>
    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        Dictionary<string, string> answers = new(StringComparer.Ordinal);
        if (!File.Exists(path))
            return answers;

        foreach (var text in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var line = JsonSerializer.Deserialize<Line>(text, Json)
                ?? throw new InvalidDataException($"A line of '{path}' is not an answer.");
            answers[line.Key] = line.Answer;
        }

        return answers;
    }

    public static void Write(string path, IReadOnlyDictionary<string, string> answers)
    {
        var text = new StringBuilder();
        foreach (var (key, answer) in answers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            text.Append(JsonSerializer.Serialize(new Line(key, answer), Json)).Append('\n');

        File.WriteAllText(path, text.ToString());
    }
}
