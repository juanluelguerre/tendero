using System.Text.Json;
using ElGuerre.Tendero.Search.Images;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

/// <summary>
/// The gate on asking OWLv2 for a kind (the camera search, CS-2): our tokenizer
/// against what the maker's own gives, id for id. As with SigLIP's, a tokenizer
/// that is nearly right produces sixteen ids and the wrong ones, and the detector
/// then looks for something else without a sign. The reference was written by
/// the maker's tokenizer at the pinned revision (<c>src/AppHost/scripts/prepare-owlv2.py</c>).
///
/// It needs the maker's vocabulary and merges, which the export leaves in
/// <c>models/owlv2</c>: where they are missing it says how to make them and skips.
/// </summary>
public sealed class Owlv2TextTokenizerTests
{
    internal const string Variable = "TENDERO_OWLV2_MODELS";

    internal sealed record Fixture(string Model, string Revision, int Length, string Prompt, IReadOnlyList<Case> Tokens, IReadOnlyList<Detection> Detections);

    internal sealed record Case(string Text, long[] Ids);

    internal sealed record Detection(string File, int Width, int Height, IReadOnlyList<Found> Found);

    internal sealed record Found(string Words, double Logit, double[] Box);

    internal static readonly Fixture Reference = JsonSerializer.Deserialize<Fixture>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Images", "owlv2-fixture.json")), JsonSerializerOptions.Web)!;

    internal static string Folder()
    {
        var folder = Environment.GetEnvironmentVariable(Variable);
        if (folder is null)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Tendero.slnx")))
                root = root.Parent;
            folder = Path.Combine(root!.FullName, "models", "owlv2");
        }

        if (!File.Exists(Path.Combine(folder, Owlv2TextTokenizer.VocabularyFile)))
            Assert.Skip($"No OWLv2 export at '{folder}'. Run src/AppHost/scripts/prepare-owlv2.py (its header says how), or set {Variable}.");

        return folder;
    }

    [Fact]
    public void The_reference_is_of_the_pinned_model_and_its_window()
    {
        Assert.Equal((Owlv2ObjectDetector.Model, Owlv2ObjectDetector.Revision, Owlv2TextTokenizer.Length), (Reference.Model, Reference.Revision, Reference.Length));
        Assert.Equal("a photo of a {}", Reference.Prompt);
        Assert.Equal("a photo of a desk lamp", Owlv2ObjectDetector.Asked("desk lamp"));
        Assert.True(Reference.Tokens.Count >= 20);
        Assert.All(Reference.Tokens, expected => Assert.Equal(Owlv2TextTokenizer.Length, expected.Ids.Length));
    }

    public static TheoryData<string> Texts() => [.. Reference.Tokens.Select(c => c.Text)];

    [Theory]
    [MemberData(nameof(Texts))]
    [Trait("Category", "Model")]
    public void Each_text_has_the_makers_ids(string text)
    {
        var tokenizer = Owlv2TextTokenizer.Load(Folder());

        Assert.Equal(Reference.Tokens.Single(c => c.Text == text).Ids, tokenizer.Encode(text));
    }
}
