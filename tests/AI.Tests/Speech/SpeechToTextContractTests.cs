using ElGuerre.Tendero.AI.Speech;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests.Speech;

/// <summary>
/// What every engine behind <see cref="ISpeechToText"/> must do (the voice spec,
/// V-1). The model is swappable by the author's ruling, so a new engine inherits
/// this before it is used: its name is stable, silence and a tone are nothing
/// heard rather than words made up, and a cancelled question stops.
/// </summary>
public abstract class SpeechToTextContractTests
{
    /// <summary>The engine, or null when it is not running here (the test then skips with the reason).</summary>
    protected abstract ISpeechToText? Engine();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ISpeechToText Running()
    {
        var engine = Engine();
        Assert.SkipWhen(engine is null, "This engine is not running here.");
        return engine!;
    }

    [Fact]
    public void Its_name_is_stable_and_lowercase()
    {
        var engine = Running();

        Assert.Matches("^[a-z0-9-]+$", engine.Name);
        Assert.True(engine.IsConfigured);
    }

    /// <summary>A running engine says it is ready: the shop draws its microphone from this.</summary>
    [Fact]
    public async Task A_running_engine_is_ready() => Assert.True(await Running().IsReadyAsync(Ct));

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Silence_is_nothing_heard(string culture)
    {
        var heard = await Running().TranscribeAsync(SpokenAudio.Read(Wav.Silence(1.5)).Audio!, culture, Ct);

        Assert.False(heard.Anything, $"Silence was heard as '{heard.Words}'.");
    }

    [Fact]
    public async Task A_tone_is_nothing_heard()
    {
        var heard = await Running().TranscribeAsync(SpokenAudio.Read(Wav.Tone(1.5)).Audio!, "es", Ct);

        Assert.False(heard.Anything, $"A tone was heard as '{heard.Words}'.");
    }

    [Fact]
    public async Task A_cancelled_question_stops()
    {
        var engine = Running();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.TranscribeAsync(SpokenAudio.Read(Wav.Silence(1)).Audio!, "es", cancelled.Token));
    }
}

/// <summary>The scripted engine, for the browser specs and every machine with no model.</summary>
public sealed class ScriptedSpeechToTextContractTests : SpeechToTextContractTests
{
    protected override ISpeechToText Engine() => new ScriptedSpeechToText(new Dictionary<string, string>());

    [Fact]
    public async Task It_answers_the_words_its_script_gives_for_that_audio()
    {
        var audio = SpokenAudio.Read(Wav.Tone(1)).Audio!;
        var engine = new ScriptedSpeechToText(new Dictionary<string, string> { [audio.Fingerprint] = "zapatillas para correr" });

        var heard = await engine.TranscribeAsync(audio, "es", TestContext.Current.CancellationToken);

        Assert.Equal(("zapatillas para correr", true), (heard.Words, heard.Anything));
    }
}

/// <summary>
/// Parakeet in this process, on our own ONNX export (`models/parakeet`, outside
/// git). With no export on this machine the tests skip; with one, it also has to
/// hear the fixture's two utterances as the maker's model did, word for word.
/// </summary>
public sealed class ParakeetOnnxSpeechToTextContractTests : SpeechToTextContractTests
{
    private static readonly Lazy<ISpeechToText?> Loaded = new(() =>
        ParakeetFixture.ExportDirectory is { } directory ? new ElGuerre.Tendero.AI.Speech.Parakeet.ParakeetOnnxSpeechToText(directory) : null);

    protected override ISpeechToText? Engine() => Loaded.Value;

    [Fact]
    public async Task It_hears_what_the_makers_model_heard()
    {
        Assert.SkipWhen(Loaded.Value is null, "No Parakeet export on this machine (scripts/prepare-parakeet.py).");

        foreach (var (audio, _, words) in ParakeetFixture.Utterances)
            Assert.Equal(words, (await Loaded.Value!.TranscribeAsync(audio, "en", TestContext.Current.CancellationToken)).Words);
    }
}

/// <summary>
/// The shop's own whisper.cpp server on its pinned port (Tendero__Voice=true). A
/// CI runner holds no model, so with nothing answering the tests skip.
/// </summary>
public sealed class WhisperCppSpeechToTextContractTests : SpeechToTextContractTests
{
    private static readonly Uri Server = new("http://localhost:38086/");

    private static readonly Lazy<bool> IsRunning = new(() =>
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            return http.GetAsync(Server).GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch (Exception unreachable) when (unreachable is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    });

    protected override ISpeechToText? Engine() =>
        IsRunning.Value
            ? new WhisperCppSpeechToText(new HttpClient { BaseAddress = Server, Timeout = TimeSpan.FromSeconds(30) })
            : null;

    /// <summary>
    /// Configured is not ready: the AppHost prepares the model before the server
    /// starts (minutes on a first start) and the API does not wait for it, so until
    /// the server answers the microphone is not drawn. Runs everywhere: nothing
    /// listens on the port it asks.
    /// </summary>
    [Fact]
    public async Task A_server_that_does_not_answer_is_not_ready()
    {
        ISpeechToText engine = new WhisperCppSpeechToText(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9/") });

        Assert.True(engine.IsConfigured);
        Assert.False(await engine.IsReadyAsync(TestContext.Current.CancellationToken));
    }
}
