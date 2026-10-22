using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Search.Images;

public sealed class ObjectDetectionOptions
{
    public const string Section = "Search:Objects";

    /// <summary>
    /// The least score an object needs to be one (ADR 0062). **0.4, provisional**,
    /// taken by the author on 2026-10-19 from the suite: three of every four objects
    /// in a real scene, less than one invented a photograph; 0.5 halved what was
    /// found (<c>docs/analysis/object-detectors.md</c>). Revisited with more scenes.
    /// </summary>
    public double MinScore { get; set; } = 0.4;
}

public static class ObjectDetectionServiceCollectionExtensions
{
    public const string Section = ObjectDetectionOptions.Section;

    /// <summary>
    /// The detectors behind <see cref="IObjectDetector"/>, each by its name
    /// (invariant 3), and the one <c>Search:Objects:Detector</c> names as the
    /// shop's. Nothing named, or a detector whose export is not whole, is
    /// <see cref="NoObjectDetector"/>: a photograph is then searched as one object,
    /// whole (the camera search, CS-9).
    ///
    /// SigLIP's grid and the Qwen's boxes were measured beside OWLv2 and are not
    /// registered: they stay as the suite's references (ADR 0062).
    /// </summary>
    public static IServiceCollection AddObjectDetection(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var objects = configuration.GetSection(Section);
        services.AddOptions<ObjectDetectionOptions>().Bind(objects);
        var detector = objects["Detector"] ?? String.Empty;

        // OWLv2 runs in this process: the folder of our export is the opt-in, and
        // one half written is no detector.
        if (objects["Owlv2:Directory"] is { Length: > 0 } owlv2
            && new[] { Owlv2ObjectDetector.DetectorFile, Owlv2ObjectDetector.TextFile, Owlv2TextTokenizer.VocabularyFile, Owlv2TextTokenizer.MergesFile }
                .All(file => File.Exists(Path.Combine(owlv2, file))))
            services.AddKeyedSingleton<IObjectDetector>(Owlv2ObjectDetector.Key, (_, _) => new Owlv2ObjectDetector(owlv2));

        string[] detectors = [Owlv2ObjectDetector.Key];
        if (detector.Length > 0 && !detectors.Contains(detector))
            throw new InvalidOperationException(
                $"{Section}:Detector '{detector}' is no detector this shop has: {String.Join(", ", detectors)}.");

        services.AddSingleton<IObjectDetector>(provider =>
            detector.Length > 0 && provider.GetKeyedService<IObjectDetector>(detector) is { } chosen ? chosen : new NoObjectDetector());

        return services;
    }
}
