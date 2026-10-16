using ElGuerre.Tendero.AI.Speech;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests.Speech;

/// <summary>
/// Which engine answers is configuration and nothing else (the voice spec, V-1):
/// each engine registered by its name, the one the section names resolved as
/// <see cref="ISpeechToText"/>, and none at all when nothing is said — the
/// microphone then is not drawn (invariant 8).
/// </summary>
public sealed class SpeechRegistrationTests
{
    private static ISpeechToText Resolved(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => (string?)setting.Value))
            .Build();
        return new ServiceCollection().AddSpeechToText(configuration).BuildServiceProvider().GetRequiredService<ISpeechToText>();
    }

    [Fact]
    public void Nothing_said_is_no_engine() =>
        Assert.False(Resolved().IsConfigured);

    [Fact]
    public void Whisper_cpp_with_its_address_is_the_shops_own()
    {
        var engine = Resolved(("Speech:Engine", "whisper-cpp"), ("Speech:WhisperCpp:Url", "http://localhost:38090/"));

        Assert.Equal((WhisperCppSpeechToText.Key, true), (engine.Name, engine.IsConfigured));
    }

    /// <summary>The AppHost hands the address only when the container runs: without it, no call to a server nobody started.</summary>
    [Fact]
    public void Whisper_cpp_with_no_address_is_no_engine() =>
        Assert.False(Resolved(("Speech:Engine", "whisper-cpp")).IsConfigured);

    [Fact]
    public async Task The_scripted_engine_reads_its_script_and_star_answers_any_audio()
    {
        var script = Path.Combine(Path.GetTempPath(), $"speech-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(script, """{ "*": "zapatillas para correr" }""", TestContext.Current.CancellationToken);
        try
        {
            var engine = Resolved(("Speech:Engine", "scripted"), ("Speech:Script", script));

            var heard = await engine.TranscribeAsync(SpokenAudio.Read(Wav.Tone(1)).Audio!, "es", TestContext.Current.CancellationToken);

            Assert.Equal((ScriptedSpeechToText.Key, "zapatillas para correr"), (engine.Name, heard.Words));
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public void An_engine_nobody_wrote_is_refused_at_start() =>
        Assert.Throws<InvalidOperationException>(() => Resolved(("Speech:Engine", "deep-ears")));
}
