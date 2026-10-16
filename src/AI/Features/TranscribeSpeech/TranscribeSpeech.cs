using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.AI.Speech;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.AI.Features.TranscribeSpeech;

/// <summary>The words of a spoken question, in the language asked, for the box they are written into unsent (the voice spec, V-2).</summary>
public sealed record TranscribeSpeechQuery(SpokenAudio Audio, string Culture) : IQuery<TranscriptionView>;

/// <param name="Words">What was heard, or empty when nothing was.</param>
/// <param name="Heard">Whether there are words to write into the box.</param>
/// <param name="Engine">Which engine heard it: for the measurement and the evidence of use, never for a shopper.</param>
public sealed record TranscriptionView(string Words, bool Heard, string Engine);

/// <summary>Whether the microphone may be drawn: an engine is configured. Not probed — nothing is transcribed to answer it.</summary>
public sealed record SpeechAvailability(bool Available);

/// <summary>The ways the shop says no to a spoken question, each with its code (ADR 0059).</summary>
public static class SpeechRefusals
{
    public static readonly Refusal NotWav = new(
        "AudioNotWav", 400, "El audio no es un WAV.", "The audio is not a WAV.");

    public static readonly Refusal WrongFormat = new(
        "AudioWrongFormat", 400, "El audio no está en el formato que se escucha.", "The audio is not in the format listened to.");

    public static readonly Refusal TooLong = new(
        "AudioTooLong", 400, "La pregunta es demasiado larga.", "The question is too long.");

    public static readonly Refusal Empty = new(
        "AudioEmpty", 400, "No se ha grabado nada.", "Nothing was recorded.");

    public static readonly Refusal NotAvailable = new(
        "SpeechNotAvailable", 503, "Aquí no se puede hablar a la tienda.", "Speaking to the shop is not available here.");

    public static readonly Refusal NotAnswering = new(
        "SpeechNotAnswering", 503, "El reconocimiento de voz no responde ahora.", "Speech recognition is not answering right now.");

    public static ProblemHttpResult Of(AudioRefusal reason) => reason switch
    {
        AudioRefusal.NotWav => Problem(NotWav,
            "Se escucha un WAV de 16 kHz, mono y 16 bits.", "A 16 kHz, mono, 16-bit WAV is listened to."),
        AudioRefusal.WrongFormat => Problem(WrongFormat,
            "Se escucha un WAV de 16 kHz, mono y 16 bits.", "A 16 kHz, mono, 16-bit WAV is listened to."),
        AudioRefusal.TooLong => Problem(TooLong,
            $"Una pregunta dura como mucho {SpokenAudio.MaxSeconds:0} segundos.", $"A question is at most {SpokenAudio.MaxSeconds:0} seconds."),
        _ => Problem(Empty,
            "Mantén pulsado el micrófono mientras hablas, o escribe la pregunta.", "Speak while the microphone listens, or type the question."),
    };

    public static ProblemHttpResult Problem(Refusal refusal, string es, string en) =>
        TypedResults.Problem(statusCode: refusal.Status, extensions: refusal.Said(es: es, en: en));
}

/// <summary>
/// Speaking to the shop (phase 14): a spoken question in, its words out, for the
/// search box on a phone and for Tino's composer, where they are written unsent.
///
/// **Anyone may ask**, as anyone may search: a guest with a voice is a shopper.
/// What bounds it is how much is asked — an engine runs for each question — never
/// who asks. **The audio is held for this request and by nothing else**: never
/// written to a store, a log or a trace (<c>docs/privacy.md</c>).
///
/// **Without an engine it says so, and nothing else changes** (invariant 8): the
/// status says the microphone is not to be drawn, and a question sent anyway is a
/// 503 with its code. Typing never passes through here.
/// </summary>
public sealed class TranscribeSpeechEndpoint : ICarterModule
{
    /// <summary>The rate limit's name: questions a minute per client, registered by the host.</summary>
    public const string PerClient = "speech";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/speech/transcriptions?culture=es — the body is the WAV itself (audio/wav)
        app.MapPost("/api/speech/transcriptions",
                async Task<Results<Ok<TranscriptionView>, ProblemHttpResult>> (
                    string? culture, HttpContext http, ISpeechToText engine, IQueryDispatcher dispatcher, CancellationToken ct) =>
                {
                    if (!engine.IsConfigured)
                        return SpeechRefusals.Problem(SpeechRefusals.NotAvailable,
                            "Esta tienda se ha arrancado sin reconocimiento de voz. Escribir funciona como siempre.",
                            "This shop was started without speech recognition. Typing works as always.");

                    using var buffer = new MemoryStream();
                    await http.Request.Body.CopyToAsync(buffer, ct);
                    var read = SpokenAudio.Read(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
                    if (read.Refusal is { } reason)
                        return SpeechRefusals.Of(reason);

                    var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    try
                    {
                        var heard = await dispatcher.SendAsync(new TranscribeSpeechQuery(read.Audio!, resolved), ct);

                        http.Response.Headers.ContentLanguage = resolved;
                        http.Response.Headers.Vary = "Accept-Language";
                        return TypedResults.Ok(heard);
                    }
                    catch (SpeechUnavailableException)
                    {
                        // The failure's detail is on the exception, logged whole; it names a
                        // server and a port, and says nothing to a shopper.
                        return SpeechRefusals.Problem(SpeechRefusals.NotAnswering,
                            "Escribir funciona como siempre. Prueba a hablar de nuevo dentro de un momento.",
                            "Typing works as always. Try speaking again in a moment.");
                    }
                })
            // By explicit decision: anyone may speak a question, as anyone may type one.
            .AllowAnonymous()
            .RequireRateLimiting(PerClient)
            .Accepts<byte[]>("audio/wav")
            // The longest question and a little; more is refused while it is read.
            .WithMetadata(new RequestSizeLimitAttribute(SpokenAudio.MaxBytes))
            .WithMetadata(new Refuses(
                SpeechRefusals.NotWav, SpeechRefusals.WrongFormat, SpeechRefusals.TooLong, SpeechRefusals.Empty,
                SpeechRefusals.NotAvailable, SpeechRefusals.NotAnswering))
            // Nothing here rides on a cookie: it reads no session and changes nothing.
            .DisableAntiforgery()
            .WithTags("Speech")
            .WithName("TranscribeSpeech");

        // GET /api/speech/status — whether a microphone may be drawn here.
        app.MapGet("/api/speech/status",
                Ok<SpeechAvailability> (ISpeechToText engine) => TypedResults.Ok(new SpeechAvailability(engine.IsConfigured)))
            .AllowAnonymous()
            .WithTags("Speech")
            .WithName("GetSpeechAvailability");
    }
}

public sealed class TranscribeSpeechHandler(ISpeechToText engine) : IQueryHandler<TranscribeSpeechQuery, TranscriptionView>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.AI);

    public async Task<TranscriptionView> HandleAsync(TranscribeSpeechQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("speech.transcribe");
        // What the question was like, and nothing of what was said.
        activity?.SetTag("speech.engine", engine.Name);
        activity?.SetTag("speech.culture", query.Culture);
        activity?.SetTag("speech.seconds", Math.Round(query.Audio.Length.TotalSeconds, 1));

        var heard = await engine.TranscribeAsync(query.Audio, query.Culture, cancellationToken);
        activity?.SetTag("speech.heard", heard.Anything);
        return new TranscriptionView(heard.Words, heard.Anything, engine.Name);
    }
}
