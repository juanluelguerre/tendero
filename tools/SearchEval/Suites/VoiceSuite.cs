using System.Diagnostics;
using System.Globalization;
using System.Text;
using ElGuerre.Tendero.AI.Speech;
using ElGuerre.Tendero.AI.Speech.Parakeet;
using ElGuerre.Tendero.SearchEval.Voice;

namespace ElGuerre.Tendero.SearchEval.Suites;

/// <summary>
/// Speaking to the shop, measured (phase 14, V-5; docs/specs/voice.md): the
/// author's recordings through each engine behind <see cref="ISpeechToText"/> —
/// the same interface the shop uses, so a new engine is measured the same way —
/// and, for each, the word error rate per language, the time from the end of the
/// question to its words, and every line it heard otherwise. Not a gate: its
/// numbers are what the author chooses the default engine with.
///
///   node tools/voice-eval/prepare.mjs
///   dotnet run --project tools/SearchEval -- --suite voice --whisper http://localhost:38086 --parakeet models/parakeet
///
/// The browser's own engine is not here: it hears only a microphone, and neither a
/// fake one nor a track reached words (docs/analysis/voice-models.md says how it was tried).
/// </summary>
public sealed class VoiceSuite : IEvaluationSuite
{
    public string Name => "voice";

    public string Description => "the author's recordings through each speech engine: word error rate and latency";

    public TimeSpan Budget => TimeSpan.FromMinutes(30);

    public async Task<int> RunAsync(EvaluationOptions options, CancellationToken cancellationToken)
    {
        var engines = Engines(options);
        if (engines.Count == 0)
        {
            Console.Error.WriteLine("Nothing to measure: give --whisper <url>, --parakeet <dir>, or both.");
            return 2;
        }

        var lines = VoiceLines.Parse(await File.ReadAllTextAsync(options.Lines!, cancellationToken));
        var recorded = lines.Where(line => File.Exists(Path.Combine(options.Recordings!, $"{line.Id}.wav"))).ToList();
        Console.WriteLine($"{recorded.Count} of {lines.Count} lines recorded in {options.Recordings}.");

        var results = new List<Heard>();
        foreach (var (name, engine) in engines)
        {
            // The first question pays for loading the model; it is asked once and not counted.
            if (recorded.FirstOrDefault() is { } warm)
                await engine.TranscribeAsync(Audio(options, warm), warm.Culture, cancellationToken);

            foreach (var line in recorded)
            {
                var audio = Audio(options, line);
                var clock = Stopwatch.StartNew();
                var heard = await engine.TranscribeAsync(audio, line.Culture, cancellationToken);
                clock.Stop();
                var said = Transcripts.Normalise(line.Said, line.Culture);
                var got = Transcripts.Normalise(heard.Words, line.Culture);
                var (edits, words) = Transcripts.Errors(said, got);
                results.Add(new Heard(name, line, heard.Words, edits, words, clock.Elapsed, audio.Length));
                Console.WriteLine($"{name,-14} {line.Id}  {clock.Elapsed.TotalMilliseconds,7:0} ms  {edits}/{words}  {heard.Words}");
            }

            // An engine in this process is measured here; one in a container, by its own stats.
            if (engine is IDisposable inProcess)
            {
                Console.WriteLine($"{name}: this process peaked at {Process.GetCurrentProcess().PeakWorkingSet64 / (1024.0 * 1024 * 1024):0.00} GiB");
                inProcess.Dispose();
            }
        }

        var report = Report(results);
        Console.WriteLine();
        Console.WriteLine(report);
        if (options.ReportPath is { Length: > 0 } path)
            await File.WriteAllTextAsync(path, report, cancellationToken);
        return 0;
    }

    private sealed record Heard(string Engine, VoiceLine Line, string Words, int Edits, int Said, TimeSpan Took, TimeSpan Length);

    private static SpokenAudio Audio(EvaluationOptions options, VoiceLine line)
    {
        var read = SpokenAudio.Read(File.ReadAllBytes(Path.Combine(options.Recordings!, $"{line.Id}.wav")));
        return read.Audio ?? throw new InvalidOperationException($"{line.Id}.wav is not a question the shop listens to: {read.Refusal}.");
    }

    private static List<(string Name, ISpeechToText Engine)> Engines(EvaluationOptions options)
    {
        var engines = new List<(string, ISpeechToText)>();
        if (options.Whisper is { Length: > 0 } whisper)
            engines.Add(("whisper-cpp", new WhisperCppSpeechToText(new HttpClient
            {
                BaseAddress = new Uri(whisper.EndsWith('/') ? whisper : whisper + "/"),
                Timeout = TimeSpan.FromMinutes(2),
            })));
        if (options.Parakeet is { Length: > 0 } parakeet)
            engines.Add(("parakeet-onnx", new ParakeetOnnxSpeechToText(parakeet)));
        return engines;
    }

    private static string Report(IReadOnlyList<Heard> results)
    {
        var text = new StringBuilder();
        text.AppendLine("| engine | language | lines | word error rate | lines exact | median | p90 | real-time factor |");
        text.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var group in results.GroupBy(result => (result.Engine, result.Line.Culture)))
        {
            var lines = group.ToList();
            var rate = (double)lines.Sum(line => line.Edits) / Math.Max(1, lines.Sum(line => line.Said));
            var took = lines.Select(line => line.Took.TotalMilliseconds).Order().ToArray();
            var factor = lines.Sum(line => line.Took.TotalSeconds) / Math.Max(1e-9, lines.Sum(line => line.Length.TotalSeconds));
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {group.Key.Engine} | {group.Key.Culture} | {lines.Count} | {rate:P1} | {lines.Count(line => line.Edits == 0)} | {Percentile(took, 0.5):0} ms | {Percentile(took, 0.9):0} ms | {factor:0.00} |"));
        }

        text.AppendLine();
        text.AppendLine("Lines heard otherwise (normalised words compared; the said line, then each engine's words):");
        text.AppendLine();
        foreach (var line in results.Where(result => result.Edits > 0).Select(result => result.Line).Distinct())
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- **{line.Id}** *{line.Said}*"));
            foreach (var heard in results.Where(result => result.Line == line))
                text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  - {heard.Engine} ({heard.Edits}/{heard.Said}): {heard.Words}"));
        }

        return text.ToString();
    }

    private static double Percentile(double[] sorted, double share) =>
        sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(share * sorted.Length) - 1)];
}
