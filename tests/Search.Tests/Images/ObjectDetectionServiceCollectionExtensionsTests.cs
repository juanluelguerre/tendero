using ElGuerre.Tendero.Search.Images;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

public sealed class ObjectDetectionServiceCollectionExtensionsTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("tendero-owlv2-").FullName;

    private static IObjectDetector Resolved(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => KeyValuePair.Create(setting.Key, (string?)setting.Value)))
            .Build();
        return new ServiceCollection().AddObjectDetection(configuration).BuildServiceProvider().GetRequiredService<IObjectDetector>();
    }

    private void Export(params string[] files)
    {
        foreach (var file in files)
            File.WriteAllBytes(Path.Combine(this.folder, file), []);
    }

    [Fact]
    public void A_shop_started_without_a_detector_has_the_one_that_says_so()
    {
        Assert.False(Resolved().IsConfigured);
    }

    [Fact]
    public void The_named_detector_answers_where_its_export_is_whole()
    {
        Export(Owlv2ObjectDetector.DetectorFile, Owlv2ObjectDetector.TextFile, Owlv2TextTokenizer.VocabularyFile, Owlv2TextTokenizer.MergesFile);

        var detector = Resolved(("Search:Objects:Detector", "owlv2"), ("Search:Objects:Owlv2:Directory", this.folder));

        Assert.IsType<Owlv2ObjectDetector>(detector);
    }

    /// <summary>One half written is no detector, as SigLIP's and Parakeet's: it would fail on its first picture.</summary>
    [Fact]
    public void An_export_half_written_is_no_detector()
    {
        Export(Owlv2ObjectDetector.DetectorFile);

        Assert.False(Resolved(("Search:Objects:Detector", "owlv2"), ("Search:Objects:Owlv2:Directory", this.folder)).IsConfigured);
    }

    [Fact]
    public void A_detector_the_shop_does_not_have_is_refused_when_it_starts()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => Resolved(("Search:Objects:Detector", "yolo")));

        Assert.Contains("owlv2", refused.Message, StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(this.folder, recursive: true);
}
