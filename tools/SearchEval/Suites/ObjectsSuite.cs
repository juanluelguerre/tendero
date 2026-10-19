using System.Diagnostics;
using System.Globalization;
using System.Text;
using ElGuerre.Tendero.AI.Chat;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.SearchEval.Objects;

namespace ElGuerre.Tendero.SearchEval.Suites;

/// <summary>
/// The objects in a photograph, measured (docs/specs/camera-search.md, CS-2): each
/// detector behind <see cref="IObjectDetector"/> — the interface the shop will
/// use, so a candidate is measured the way it would run — over ABO's studio
/// photographs and the author's scenes, reported apart, because studio
/// photographs flatter a detector. Not a gate: the author chooses the detector
/// with these numbers, and the floors are read from them.
///
///   node tools/picture-eval/prepare.mjs abo
///   dotnet run --project tools/SearchEval -- --suite objects --siglip models/siglip2
/// </summary>
public sealed class ObjectsSuite : IEvaluationSuite
{
    /// <summary>The floors each detector is read at: the search's own floor is chosen from these rows.</summary>
    private static readonly double[] Floors = [0.1, 0.3, 0.4, 0.5, 0.7];

    public string Name => "objects";

    public string Description => "the objects in a photograph, per detector: kinds right, objects found and invented, latency";

    public TimeSpan Budget => TimeSpan.FromMinutes(60);

    public async Task<int> RunAsync(EvaluationOptions options, CancellationToken cancellationToken)
    {
        var detectors = Detectors(options);
        if (detectors.Count == 0)
        {
            Console.Error.WriteLine("Nothing to measure: give --siglip <dir>, --owl <dir>, --vision <url>, or any of them.");
            return 2;
        }

        var kinds = PictureCases.Kinds();
        var parents = PictureCases.Parents(options.CategoriesPath!);
        IReadOnlyList<PictureCase> all = [.. PictureCases.Abo(options.Pictures!), .. PictureCases.Author()];
        var cases = all.Where(c => File.Exists(c.File)).ToList();
        Console.WriteLine($"{cases.Count} of {all.Count} photographs prepared ({cases.Count(c => c.Source == "abo")} ABO, {cases.Count(c => c.Source == "author")} the author's); {kinds.Count} kinds.");

        var results = new List<Answer>();
        foreach (var detector in detectors)
        {
            // The first photograph pays for loading the model; it is asked once and not counted.
            if (cases.FirstOrDefault() is { } warm)
                await detector.DetectAsync(await File.ReadAllBytesAsync(warm.File, cancellationToken), kinds, cancellationToken);

            foreach (var picture in cases)
            {
                var bytes = await File.ReadAllBytesAsync(picture.File, cancellationToken);
                var clock = Stopwatch.StartNew();
                var found = await detector.DetectAsync(bytes, kinds, cancellationToken);
                clock.Stop();
                var answer = new Answer(detector.Name, picture, found, clock.Elapsed, Floors.ToDictionary(floor => floor, floor => ObjectsScore.Of(picture.Expected, found, parents, floor)));
                results.Add(answer);
                var likeliest = ObjectsInPicture.Choose(found, floor: 0).FirstOrDefault();
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{detector.Name,-12} {picture.Source,-6} {picture.Id,-14} {clock.Elapsed.TotalMilliseconds,7:0} ms  {(answer.Scores[0.1].LikeliestRight ? "right" : "WRONG")}  expected {string.Join("+", picture.Expected)}, likeliest {likeliest?.Kind.Code} {likeliest?.Score:0.00} at {likeliest?.Box}; {found.Count} found"));
            }

            if (detector is IDisposable inProcess)
                inProcess.Dispose();
        }

        var report = Report(results);
        Console.WriteLine();
        Console.WriteLine(report);
        if (options.ReportPath is { Length: > 0 } path)
            await File.WriteAllTextAsync(path, report, cancellationToken);
        return 0;
    }

    private sealed record Answer(string Detector, PictureCase Picture, IReadOnlyList<DetectedObject> Found, TimeSpan Took, IReadOnlyDictionary<double, ObjectsScore> Scores);

    private static List<IObjectDetector> Detectors(EvaluationOptions options)
    {
        var detectors = new List<IObjectDetector>();
        if (options.Siglip is { Length: > 0 } siglip)
            detectors.Add(new SiglipGridObjectDetector(new SiglipOnnxEmbeddings(siglip)));
        // The local model with its projector, as the AppHost serves it with Tendero__Vision=true.
        if (options.Vision is { Length: > 0 } vision)
            detectors.Add(new VisionModelObjectDetector(OpenAICompatibleChat.Create(new Uri(vision), options.Model ?? "qwen3.5-4b")));
        if (options.Owl is { Length: > 0 } owl)
            detectors.Add(new Owlv2ObjectDetector(owl));
        return detectors;
    }

    private static string Report(IReadOnlyList<Answer> results)
    {
        var text = new StringBuilder();
        text.AppendLine("| detector | source | photographs | likeliest kind right | of the shop's kinds | of kinds not sold | median | p90 |");
        text.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var group in results.GroupBy(answer => (answer.Detector, answer.Picture.Source)))
        {
            var answers = group.ToList();
            var sold = answers.Where(answer => !IsNotSold(answer.Picture)).ToList();
            var notSold = answers.Where(answer => IsNotSold(answer.Picture)).ToList();
            var took = answers.Select(answer => answer.Took.TotalMilliseconds).Order().ToArray();
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {group.Key.Detector} | {group.Key.Source} | {answers.Count} | {Share(answers)} | {Share(sold)} | {Share(notSold)} | {Percentile(took, 0.5):0} ms | {Percentile(took, 0.9):0} ms |"));
        }

        text.AppendLine();
        text.AppendLine("At each floor: expected objects found, and objects invented per photograph (the floor the search uses is chosen from these).");
        text.AppendLine();
        text.AppendLine("| detector | source | floor | found | invented per photograph |");
        text.AppendLine("|---|---|---:|---:|---:|");
        foreach (var group in results.GroupBy(answer => (answer.Detector, answer.Picture.Source)))
        {
            foreach (var floor in Floors)
            {
                var scores = group.Select(answer => answer.Scores[floor]).ToList();
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {group.Key.Detector} | {group.Key.Source} | {floor:0.0} | {scores.Sum(s => s.Found)} of {scores.Sum(s => s.Expected)} | {scores.Average(s => s.Invented):0.00} |"));
            }
        }

        text.AppendLine();
        text.AppendLine("Likeliest kind right, by expected kind:");
        text.AppendLine();
        text.AppendLine("| detector | kind | right |");
        text.AppendLine("|---|---|---:|");
        foreach (var group in results.Where(answer => answer.Picture.Expected.Count == 1).GroupBy(answer => (answer.Detector, Kind: answer.Picture.Expected[0])).OrderBy(group => group.Key.Detector).ThenBy(group => group.Key.Kind, StringComparer.Ordinal))
            text.AppendLine($"| {group.Key.Detector} | {group.Key.Kind} | {group.Count(answer => answer.Scores[Floors[0]].LikeliestRight)} of {group.Count()} |");

        return text.ToString();
    }

    /// <summary>A kind the shop does not sell is written in lower case in the kinds file, a category code in upper case.</summary>
    private static bool IsNotSold(PictureCase picture) => picture.Expected.All(kind => kind.Any(char.IsLower));

    private static string Share(IReadOnlyList<Answer> answers) =>
        answers.Count == 0 ? "—" : string.Create(CultureInfo.InvariantCulture, $"{answers.Count(answer => answer.Scores[Floors[0]].LikeliestRight)} of {answers.Count}");

    private static double Percentile(double[] sorted, double share) =>
        sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(share * sorted.Length) - 1)];
}
