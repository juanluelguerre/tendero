using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// What the model is actually asked for.
/// </summary>
public sealed class PromptTests
{
    private static ColourLexicon Lexicon =>
        ColourLexicon.Read(Path.Combine(RepositoryRoot.Find(), "seed", "attributes.sample.json"));

    private static SeedProduct Shoes => new(
        "B073WXYZ01",
        "Pulse Runner cushioned running shoes",
        "Neutral running shoes with responsive foam midsole, breathable mesh upper and reinforced heel counter. 8 mm drop.",
        "azul marino");

    private static SeedProduct SpiceRack => new(
        "B14ORG0901",
        "Two-tier bamboo spice rack",
        "Two-tier bamboo rack that lets you see the back row. It fits inside a standard cupboard.",
        null);

    /// <summary>
    /// **The subject goes first, and this is the load-bearing test.** CLIP takes
    /// 77 tokens and throws the rest away without saying so, and the style block
    /// is long — six hex codes, each of which the tokenizer splits digit by
    /// digit. With the subject last, what gets discarded is the colour and the
    /// product: ninety-two beautiful illustrations of nothing in particular.
    /// </summary>
    [Fact]
    public void The_subject_comes_first_so_truncation_costs_style_and_not_the_product()
    {
        var prompt = PromptTemplate.Positive(Shoes, Lexicon);

        var subject = prompt.IndexOf("running shoes", StringComparison.Ordinal);
        var style = prompt.IndexOf("warm grey background", StringComparison.Ordinal);

        Assert.True(subject >= 0 && style >= 0, prompt);
        Assert.True(subject < style, $"The subject must precede the style block.\n\n{prompt}");
    }

    [Fact]
    public void A_declared_colour_is_named_in_english()
    {
        var prompt = PromptTemplate.Positive(Shoes, Lexicon);

        Assert.Contains("navy blue", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("azul marino", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The thirty-five without a colour. A builder that interpolates a null
    /// writes "The object is : a bamboo rack", and a colon about nothing is a
    /// token the model spends on punctuation.
    /// </summary>
    [Fact]
    public void A_product_without_a_colour_carries_no_dangling_clause()
    {
        var prompt = PromptTemplate.Positive(SpiceRack, Lexicon);

        Assert.DoesNotContain("is :", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bamboo rack", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The register is published in <c>seed/IMAGES-TODO.md</c> so a person can
    /// read what the hundred images are supposed to look like. If the code and
    /// the document drift, the document is the one people believe.
    /// </summary>
    [Fact]
    public void Every_style_line_appears_in_the_published_specification()
    {
        var published = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "seed", "IMAGES-TODO.md"));

        var missing = PromptTemplate.StyleLines
            .Where(line => !published.Contains(line, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"These lines are in the code and not in seed/IMAGES-TODO.md:\n  {string.Join("\n  ", missing)}");
    }

    /// <summary>
    /// **The negations live here and not in the positive prompt.** CLIP has no
    /// reliable negation: "no text, no logos" puts "text" and "logo" into the
    /// embedding like any other word. What actually pushes away from a concept is
    /// classifier-free guidance against a separate negative.
    /// </summary>
    [Fact]
    public void The_negative_prompt_carries_what_the_positive_cannot_say()
    {
        Assert.Contains("text", PromptTemplate.Negative, StringComparison.Ordinal);
        Assert.Contains("logo", PromptTemplate.Negative, StringComparison.Ordinal);
        Assert.Contains("watermark", PromptTemplate.Negative, StringComparison.Ordinal);

        Assert.DoesNotContain("No text", PromptTemplate.Positive(Shoes, Lexicon), StringComparison.Ordinal);
    }
}
