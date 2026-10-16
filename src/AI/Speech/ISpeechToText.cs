namespace ElGuerre.Tendero.AI.Speech;

/// <summary>
/// Words from a spoken question (the voice spec, V-1). **Every engine and every
/// model behind this**, by the author's ruling of 2026-10-16: nothing outside an
/// adapter names whisper.cpp or a Whisper revision. A new engine is one class and
/// one keyed registration, and inherits <c>SpeechToTextContractTests</c> before it
/// is used; the model an engine loads is its options, never code.
/// </summary>
public interface ISpeechToText
{
    /// <summary>The engine's stable name — <c>whisper-cpp</c>, <c>scripted</c> — for the span, the measurement and the transcript's answer.</summary>
    string Name { get; }

    /// <summary>Whether this process was started with an engine at all. Configured, not probed: nothing is transcribed to answer it.</summary>
    bool IsConfigured { get; }

    /// <summary>The words in the audio, in the language asked; nothing heard is an answer, not a failure.</summary>
    /// <exception cref="SpeechUnavailableException">The engine is configured and does not answer.</exception>
    Task<Heard> TranscribeAsync(SpokenAudio audio, string culture, CancellationToken cancellationToken = default);
}

/// <summary>What was heard: words, or nothing. Never a half-written question.</summary>
public sealed record Heard(string Words)
{
    public static readonly Heard Nothing = new(string.Empty);

    public bool Anything => Words.Length > 0;
}

/// <summary>A configured engine that does not answer: the transcript is not available right now, and the text box is unchanged.</summary>
public sealed class SpeechUnavailableException(string engine, string reason)
    : Exception($"Speech to text ({engine}) is not answering: {reason}")
{
    public string Engine { get; } = engine;
}

/// <summary>The engine of a process started without one: never asked, and it says so.</summary>
public sealed class NoSpeechToText : ISpeechToText
{
    public string Name => "none";

    public bool IsConfigured => false;

    public Task<Heard> TranscribeAsync(SpokenAudio audio, string culture, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No speech engine is configured; ask IsConfigured first.");
}
