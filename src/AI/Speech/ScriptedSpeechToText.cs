namespace ElGuerre.Tendero.AI.Speech;

/// <summary>
/// An engine that answers from a script, by the audio's fingerprint, and hears
/// nothing in audio it was not given words for. For the browser specs and every
/// machine with no model, as Tino's and Steward's scripts are.
///
/// <see cref="AnyAudio"/> answers audio the script does not list: a browser
/// re-encodes the microphone, so a spec cannot know its fingerprint beforehand.
/// </summary>
public sealed class ScriptedSpeechToText(IReadOnlyDictionary<string, string> words) : ISpeechToText
{
    public const string Key = "scripted";

    /// <summary>The script's key for any audio it does not list by fingerprint.</summary>
    public const string AnyAudio = "*";

    public string Name => Key;

    public bool IsConfigured => true;

    public Task<Heard> TranscribeAsync(SpokenAudio audio, string culture, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var heard = words.TryGetValue(audio.Fingerprint, out var listed) ? listed
            : words.TryGetValue(AnyAudio, out var any) ? any
            : null;
        return Task.FromResult(heard is null ? Heard.Nothing : new Heard(heard.Trim()));
    }

    /// <summary>A script file: a JSON object of fingerprints (or <see cref="AnyAudio"/>) to words.</summary>
    public static ScriptedSpeechToText FromFile(string path) =>
        new(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"The speech script {path} is empty."));
}
