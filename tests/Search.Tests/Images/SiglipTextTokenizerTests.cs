using System.Text.Json;
using ElGuerre.Tendero.Search.Images;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

/// <summary>
/// **The gate on embedding text in process** (phase 13a, P13-1): our tokenizer
/// against what the maker's own gives, id for id, for texts a shopper types in
/// both of the shop's languages — accents, digits, capitals, runs of spaces, an
/// emoji, nothing at all, and a text longer than the window.
///
/// A tokenizer that is nearly right produces ids, produces sixty-four of them,
/// and produces the wrong ones: text searched against pictures then degrades
/// without a sign anywhere. So the reference is not typed here. It was written
/// by the maker's tokenizer at the pinned revision
/// (`src/AppHost/scripts/siglip-tokenizer-fixture.py`) and is committed beside
/// this test; a second copy typed by hand would keep passing after the first
/// changed.
///
/// It needs the tokenizer's own file, 4 MB that are the maker's and not the
/// repository's: where it is missing the test says how to fetch it and skips.
/// </summary>
public sealed class SiglipTextTokenizerTests
{
    private const string Variable = "TENDERO_SIGLIP_MODELS";

    private sealed record Fixture(string Model, string Revision, int Length, int Pad, int Eos, IReadOnlyList<Case> Cases);

    private sealed record Case(string Text, long[] Ids);

    private static readonly Fixture Reference = JsonSerializer.Deserialize<Fixture>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Images", "siglip-tokenizer-fixture.json")), JsonSerializerOptions.Web)!;

    private static SiglipTextTokenizer Load()
    {
        var folder = Environment.GetEnvironmentVariable(Variable);
        if (folder is null)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Tendero.slnx")))
                root = root.Parent;
            folder = Path.Combine(root!.FullName, "models", "siglip2");
        }

        var model = Path.Combine(folder, SiglipTextTokenizer.FileName);
        if (!File.Exists(model))
            Assert.Skip($"No SigLIP 2 tokenizer at '{folder}'. Run src/AppHost/scripts/siglip-tokenizer-fixture.py (its header says how), or set {Variable}.");

        return SiglipTextTokenizer.Load(model);
    }

    /// <summary>The fixture is of the model the shop pins, and of the window it embeds with: a fixture of another would hold nothing.</summary>
    [Fact]
    public void The_reference_is_of_the_pinned_model_and_its_window()
    {
        Assert.Equal((SiglipTextTokenizer.Model, SiglipTextTokenizer.Revision, SiglipTextTokenizer.Length), (Reference.Model, Reference.Revision, Reference.Length));
        Assert.True(Reference.Cases.Count >= 20);
        Assert.All(Reference.Cases, expected => Assert.Equal(SiglipTextTokenizer.Length, expected.Ids.Length));
    }

    [Fact]
    public void Every_text_gets_the_ids_the_makers_tokenizer_gives_it()
    {
        var tokenizer = Load();

        var wrong = Reference.Cases
            .Select(expected => (expected.Text, Expected: expected.Ids, Actual: tokenizer.Encode(expected.Text)))
            .Where(result => !result.Expected.SequenceEqual(result.Actual))
            .Select(result => $"\"{result.Text}\": expected [{string.Join(' ', result.Expected.TakeWhile(id => id != Reference.Pad))}], got [{string.Join(' ', result.Actual.TakeWhile(id => id != Reference.Pad))}]")
            .ToList();

        Assert.True(wrong.Count == 0, $"{wrong.Count} of {Reference.Cases.Count} texts differ:\n" + string.Join('\n', wrong));
    }

    /// <summary>What the shape of an answer has to be whatever the text: the window's length, the end marked once, and padding after it.</summary>
    [Fact]
    public void A_text_is_its_ids_the_end_marker_and_padding_to_the_window()
    {
        var tokenizer = Load();

        var (nothing, word, longer) = (tokenizer.Encode(""), tokenizer.Encode("mochila"), tokenizer.Encode(string.Join(' ', Enumerable.Repeat("mochila", 200))));

        Assert.Equal((long)Reference.Eos, nothing[0]);
        Assert.All(nothing.Skip(1), id => Assert.Equal(Reference.Pad, id));
        Assert.Equal(SiglipTextTokenizer.Length, word.Length);
        Assert.Equal(1, word.Count(id => id == Reference.Eos));
        // Cut at the window, and still ended as the maker ends it.
        Assert.Equal(SiglipTextTokenizer.Length, longer.Length);
        Assert.Equal((long)Reference.Eos, longer[^1]);
        Assert.DoesNotContain((long)Reference.Pad, longer);
    }
}
