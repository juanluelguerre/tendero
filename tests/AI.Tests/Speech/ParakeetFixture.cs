using System.Text.Json;
using ElGuerre.Tendero.AI.Speech;

namespace ElGuerre.Tendero.AI.Tests.Speech;

/// <summary>
/// What the maker's model made of two LibriSpeech utterances (CC BY 4.0), written
/// by <c>scripts/prepare-parakeet.py</c> beside the export: the .NET side is held
/// to it. And where the export itself is, when this machine has one.
/// </summary>
internal static class ParakeetFixture
{
    private static readonly Lazy<JsonElement> Document = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Speech", "parakeet-fixture.json"))).RootElement);

    public static IReadOnlyList<(SpokenAudio Audio, float[] Samples, string Words)> Utterances =>
    [
        .. Document.Value.GetProperty("utterances").EnumerateArray().Select(utterance =>
        {
            var pcm = Convert.FromBase64String(utterance.GetProperty("pcm16").GetString()!);
            var wav = Wav.Of(pcm, 16_000, 1, 16);
            var audio = SpokenAudio.Read(wav).Audio
                ?? throw new InvalidOperationException("A fixture utterance is not a question the shop listens to.");
            return (audio, audio.Samples(), utterance.GetProperty("words").GetString()!);
        }),
    ];

    public static JsonElement Features => Document.Value.GetProperty("features");

    public static double MelSum => Document.Value.GetProperty("mel").GetProperty("sum").GetDouble();

    /// <summary>The export's folder, outside git: found by walking up to the repository's solution.</summary>
    public static string? ExportDirectory
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (!File.Exists(Path.Combine(directory.FullName, "Tendero.slnx")))
                    continue;
                var export = Path.Combine(directory.FullName, "models", "parakeet");
                return File.Exists(Path.Combine(export, "encoder.onnx")) ? export : null;
            }

            return null;
        }
    }
}
