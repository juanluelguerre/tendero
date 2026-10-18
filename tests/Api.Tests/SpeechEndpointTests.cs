using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.AI.Tests.Speech;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// A spoken question over HTTP (the voice spec, V-1). The test host runs with no
/// engine, which is the case the page must survive: the microphone is not offered
/// and a question sent anyway is refused as not available — never a failure that
/// touches the text box (invariant 8). With the scripted engine, the words come
/// back for the box, unsent.
/// </summary>
public sealed class SpeechEndpointTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string script = Path.Combine(Path.GetTempPath(), $"speech-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(script);

    private static ByteArrayContent Audio(byte[] wav)
    {
        var content = new ByteArrayContent(wav);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        return content;
    }

    private async Task<HttpClient> Scripted(TenderoApiFactory factory)
    {
        await File.WriteAllTextAsync(script, """{ "*": "zapatillas para correr" }""", Ct);
        return factory.WithWebHostBuilder(host => host
                .UseSetting("Speech:Engine", "scripted")
                .UseSetting("Speech:Script", script))
            .CreateClient();
    }

    [Fact]
    public async Task With_no_engine_the_microphone_is_not_offered_and_a_question_is_not_available()
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var status = JsonNode.Parse(await client.GetStringAsync("/api/speech/status", Ct))!;
        var response = await client.PostAsync("/api/speech/transcriptions?culture=es", Audio(Wav.Silence(1)), Ct);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

        Assert.False(status["available"]!.GetValue<bool>());
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "SpeechNotAvailable"), (response.StatusCode, problem["code"]!.GetValue<string>()));
    }

    /// <summary>
    /// An engine configured whose server does not answer yet — a first start still
    /// preparing the model — offers no microphone: one drawn then would fail on the
    /// first word.
    /// </summary>
    [Fact]
    public async Task With_an_engine_that_does_not_answer_the_microphone_is_not_offered()
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.WithWebHostBuilder(host => host
                .UseSetting("Speech:Engine", "whisper-cpp")
                .UseSetting("Speech:WhisperCpp:Url", "http://127.0.0.1:9/"))
            .CreateClient();

        var status = JsonNode.Parse(await client.GetStringAsync("/api/speech/status", Ct))!;

        Assert.False(status["available"]!.GetValue<bool>());
    }

    [Fact]
    public async Task With_an_engine_the_words_come_back_in_the_language_asked()
    {
        using var factory = new TenderoApiFactory();
        using var client = await Scripted(factory);

        var status = JsonNode.Parse(await client.GetStringAsync("/api/speech/status", Ct))!;
        var response = await client.PostAsync("/api/speech/transcriptions?culture=es", Audio(Wav.Tone(1.5)), Ct);
        var heard = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

        Assert.True(status["available"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("zapatillas para correr", true, "scripted"),
            (heard["words"]!.GetValue<string>(), heard["heard"]!.GetValue<bool>(), heard["engine"]!.GetValue<string>()));
        Assert.Equal("es", response.Content.Headers.ContentLanguage.Single());
    }

    public static TheoryData<byte[], string> Refused => new()
    {
        { System.Text.Encoding.ASCII.GetBytes("not audio"), "AudioNotWav" },
        { Wav.Silence(1, sampleRate: 44_100), "AudioWrongFormat" },
        { Wav.Silence(31), "AudioTooLong" },
        { Wav.Silence(0.1), "AudioEmpty" },
    };

    /// <summary>Refused by its bytes, with a code and a sentence in the language asked, before the engine hears anything.</summary>
    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Audio_the_shop_does_not_listen_to_is_refused_with_its_code(byte[] wav, string code)
    {
        using var factory = new TenderoApiFactory();
        using var client = await Scripted(factory);

        var response = await client.PostAsync("/api/speech/transcriptions?culture=en", Audio(wav), Ct);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

        Assert.Equal((HttpStatusCode.BadRequest, code), (response.StatusCode, problem["code"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(problem["detail"]?.GetValue<string>()));
    }
}
