using System.Net;
using System.Text;
using ElGuerre.Tendero.AI.Speech;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests.Speech;

/// <summary>
/// The whisper.cpp adapter against a server it cannot tell from the real one:
/// what it sends, how it reads the answer, and what a server that is down becomes.
/// </summary>
public sealed class WhisperCppSpeechToTextTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SpokenAudio Audio() => SpokenAudio.Read(Wav.Tone(1)).Audio!;

    [Fact]
    public async Task It_posts_the_audio_with_the_language_and_reads_the_words_trimmed()
    {
        var server = new Server(HttpStatusCode.OK, """{"text":"  zapatillas para correr \n"}""");

        var heard = await Engine(server).TranscribeAsync(Audio(), "es", Ct);

        Assert.Equal("zapatillas para correr", heard.Words);
        Assert.Equal("/inference", server.Path);
        Assert.Contains("name=language", server.Body);
        Assert.Contains("es", server.Body);
        Assert.Contains("name=file", server.Body);
        Assert.Contains("RIFF", server.Body);
    }

    /// <summary>What Whisper writes for a sound with no words: a mark, never something to send.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData(" (silence) ")]
    [InlineData("[Música]")]
    public async Task A_mark_with_no_words_is_nothing_heard(string text)
    {
        var heard = await Engine(new Server(HttpStatusCode.OK, $$"""{"text":"{{text}}"}""")).TranscribeAsync(Audio(), "es", Ct);

        Assert.False(heard.Anything, $"'{text}' was heard as '{heard.Words}'.");
    }

    [Fact]
    public async Task A_server_that_fails_is_said_to_be_unavailable()
    {
        var failure = await Assert.ThrowsAsync<SpeechUnavailableException>(
            () => Engine(new Server(HttpStatusCode.InternalServerError, "boom")).TranscribeAsync(Audio(), "es", Ct));

        Assert.Equal("whisper-cpp", failure.Engine);
    }

    [Fact]
    public async Task A_server_that_is_not_there_is_said_to_be_unavailable() =>
        await Assert.ThrowsAsync<SpeechUnavailableException>(
            () => Engine(new Server(unreachable: true)).TranscribeAsync(Audio(), "es", Ct));

    private static WhisperCppSpeechToText Engine(Server server) =>
        new(new HttpClient(server) { BaseAddress = new Uri("http://whisper.test/") });

    private sealed class Server(HttpStatusCode status = HttpStatusCode.OK, string answer = "", bool unreachable = false) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (unreachable)
                throw new HttpRequestException("No connection could be made.");

            Path = request.RequestUri?.AbsolutePath;
            Body = Encoding.Latin1.GetString(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }
}
