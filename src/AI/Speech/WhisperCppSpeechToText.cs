using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace ElGuerre.Tendero.AI.Speech;

/// <summary>
/// The shop's own transcriber: a whisper.cpp server (<c>whisper-server</c>), asked
/// over its <c>/inference</c> endpoint with the audio and the language. Which
/// Whisper weights it serves is the server's start-up argument, the AppHost's to
/// pin — this adapter names no model, so another one is a configuration change and
/// a measurement (the voice spec, V-1).
/// </summary>
public sealed partial class WhisperCppSpeechToText(HttpClient http) : ISpeechToText
{
    public const string Key = "whisper-cpp";

    public string Name => Key;

    public bool IsConfigured => true;

    /// <summary>The server's own page answers once its model is loaded; two seconds, so a page that asks never waits on it.</summary>
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var response = await http.GetAsync(string.Empty, deadline.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception unreachable) when (unreachable is HttpRequestException
                                            || (unreachable is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return false;
        }
    }

    public async Task<Heard> TranscribeAsync(SpokenAudio audio, string culture, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var form = new MultipartFormDataContent();
        var file = new ReadOnlyMemoryContent(audio.Wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "question.wav");
        form.Add(new StringContent(culture), "language");
        form.Add(new StringContent("json"), "response_format");
        // The likeliest words, every time: a transcript is not where variety helps.
        form.Add(new StringContent("0"), "temperature");

        Answer? answer;
        try
        {
            using var response = await http.PostAsync("inference", form, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new SpeechUnavailableException(Key, $"the server answered {(int)response.StatusCode}");

            answer = await response.Content.ReadFromJsonAsync<Answer>(cancellationToken);
        }
        catch (Exception failure) when (failure is HttpRequestException or System.Text.Json.JsonException
                                        || (failure is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new SpeechUnavailableException(Key, $"{failure.GetType().Name}: {failure.Message}");
        }

        return Words(answer?.Text);
    }

    /// <summary>
    /// Whisper marks a sound with no words — <c>[BLANK_AUDIO]</c>, <c>(silence)</c>,
    /// <c>[Música]</c> — and a mark is never something to send.
    /// </summary>
    private static Heard Words(string? text)
    {
        var words = Marks().Replace(text ?? string.Empty, " ").Trim();
        return words.Length == 0 ? Heard.Nothing : new Heard(words);
    }

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)")]
    private static partial Regex Marks();

    private sealed record Answer(string? Text);
}
