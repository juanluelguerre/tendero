using ElGuerre.Tendero.AI.Speech.Parakeet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.AI.Speech;

public static class SpeechServiceCollectionExtensions
{
    /// <summary>
    /// The engines behind <see cref="ISpeechToText"/>, each by its name (invariant
    /// 3), and the one <c>Speech:Engine</c> names as the shop's. Nothing named, or
    /// an engine with nothing to reach, is <see cref="NoSpeechToText"/>: the
    /// microphone is then not drawn and the text box is unchanged (invariant 8).
    /// </summary>
    public static IServiceCollection AddSpeechToText(
        this IServiceCollection services, IConfiguration configuration, string section = "Speech")
    {
        var speech = configuration.GetSection(section);
        var engine = speech["Engine"] ?? string.Empty;

        // The AppHost hands the address only when the container runs.
        if (speech["WhisperCpp:Url"] is { Length: > 0 } url)
            services.AddKeyedSingleton<ISpeechToText>(WhisperCppSpeechToText.Key, (_, _) =>
                new WhisperCppSpeechToText(new HttpClient
                {
                    BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/"),
                    // A short question on a CPU: the measurement sets the real figure (V-5).
                    Timeout = TimeSpan.FromSeconds(speech.GetValue("WhisperCpp:TimeoutSeconds", 30)),
                }));

        if (speech["Script"] is { Length: > 0 } script)
            services.AddKeyedSingleton<ISpeechToText>(ScriptedSpeechToText.Key, (_, _) => ScriptedSpeechToText.FromFile(script));

        // Parakeet runs in this process: the folder of our export is the opt-in, and
        // one half written is no engine (as SigLIP's, ADR 0058).
        if (speech["Parakeet:Directory"] is { Length: > 0 } parakeet
            && new[] { "encoder.onnx", "decoder.onnx", "joint.onnx", "vocab.json" }.All(file => File.Exists(Path.Combine(parakeet, file))))
            services.AddKeyedSingleton<ISpeechToText>(ParakeetOnnxSpeechToText.Key, (_, _) => new ParakeetOnnxSpeechToText(parakeet));

        string[] engines = [WhisperCppSpeechToText.Key, ParakeetOnnxSpeechToText.Key, ScriptedSpeechToText.Key];
        if (engine.Length > 0 && !engines.Contains(engine))
            throw new InvalidOperationException(
                $"Speech:Engine '{engine}' is no engine this shop has: {string.Join(", ", engines)}.");

        services.AddSingleton<ISpeechToText>(provider =>
            engine.Length > 0 && provider.GetKeyedService<ISpeechToText>(engine) is { } chosen ? chosen : new NoSpeechToText());

        return services;
    }
}
