using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// CLIP's byte-level BPE, which SDXL uses twice.
///
/// **The oracles come from the vocabulary itself**, not from a table typed here:
/// `olive</w>` is a literal key in `vocab.json`, so the test reads the id it maps
/// to and asserts the tokenizer produces it. A hardcoded 9898 would be a second
/// copy of the artefact, and it would keep passing after the artefact changed.
/// </summary>
public sealed class TokenizerTests
{
    private static ClipTokenizer Load(string which = "tokenizer") =>
        ClipTokenizer.Load(ModelDirectory.Tokenizer(which));

    [Theory]
    [InlineData("olive", "olive</w>")]
    [InlineData("navy", "navy</w>")]
    [InlineData("running", "running</w>")]
    public void A_whole_word_gets_the_id_the_vocabulary_gives_it(string word, string key)
    {
        var tokenizer = Load();

        Assert.Equal([tokenizer.IdOf(key)], tokenizer.Encode(word));
    }

    /// <summary>
    /// The marker CLIP glues to the last character of every word. Without it the
    /// tokenizer still produces ids, still produces 77 of them, and produces the
    /// wrong ones — which is the failure that looks like a bad model rather than
    /// a bad tokenizer.
    /// </summary>
    [Fact]
    public void A_word_inside_a_sentence_still_carries_the_end_of_word_marker()
    {
        var tokenizer = Load();

        var alone = tokenizer.Encode("olive");
        var inside = tokenizer.Encode("an olive rack");

        Assert.Contains(alone[0], inside);
    }

    [Fact]
    public void An_empty_prompt_is_the_two_markers_and_then_padding()
    {
        var tokenizer = Load();

        var ids = tokenizer.EncodeToLength(string.Empty);

        Assert.Equal(77, ids.Length);
        Assert.Equal(tokenizer.IdOf("<|startoftext|>"), ids[0]);
        Assert.Equal(tokenizer.IdOf("<|endoftext|>"), ids[1]);
        Assert.All(ids[2..], id => Assert.Equal(tokenizer.PadId, id));
    }

    /// <summary>
    /// **The two tokenizers do not pad with the same token**, and nothing about
    /// the file names suggests it: the first uses `&lt;|endoftext|&gt;` and the
    /// second uses `!`, which is id 0. Padding a prompt with the wrong one
    /// degrades every image and raises no error anywhere.
    /// </summary>
    [Fact]
    public void The_two_tokenizers_pad_with_different_tokens()
    {
        Assert.Equal(Load("tokenizer").IdOf("<|endoftext|>"), Load("tokenizer").PadId);
        Assert.Equal(Load("tokenizer_2").IdOf("!"), Load("tokenizer_2").PadId);
        Assert.NotEqual(Load("tokenizer").PadId, Load("tokenizer_2").PadId);
    }

    [Fact]
    public void A_prompt_longer_than_the_window_is_cut_and_still_ends_in_the_marker()
    {
        var tokenizer = Load();

        var ids = tokenizer.EncodeToLength(string.Join(" ", Enumerable.Repeat("olive", 200)));

        Assert.Equal(77, ids.Length);
        Assert.Equal(tokenizer.IdOf("<|endoftext|>"), ids[^1]);
    }

    /// <summary>
    /// Why a hex code is expensive: CLIP's pre-tokenizer takes digits **one at a
    /// time**, so `#FAF9F7` is not one token and not two. The published template
    /// carried six of them, which is where its budget went.
    /// </summary>
    [Fact]
    public void A_hex_colour_costs_several_tokens_because_digits_do_not_group()
    {
        var tokenizer = Load();

        Assert.True(
            tokenizer.Encode("#FAF9F7").Length >= 5,
            $"#FAF9F7 cost {tokenizer.Encode("#FAF9F7").Length} tokens.");
    }

    /// <summary>
    /// **The ceiling is gone, and this is what replaced the guard.**
    ///
    /// The published template cost 140 to 161 tokens against a window of 75, so
    /// every one of the hundred products was truncated -- losing 65 to 86 tokens
    /// each, in silence. Trimming the style block from 123 to 36 fixed it, and
    /// then putting the product's NAME back into the subject broke it again for
    /// forty-six of them.
    ///
    /// Shaving words until a number fits is not a design. Cross-attention does
    /// not care how long the sequence is, so a long prompt is cut into whole
    /// windows, each wrapped in its own markers, and laid end to end. What is
    /// asserted now is not that everything fits in one window but that nothing
    /// needs more than two: a third would mean a prompt has grown into an essay,
    /// and every window costs another pass through an encoder.
    /// </summary>
    [Fact]
    public void No_prompt_needs_more_than_two_windows()
    {
        var tokenizer = Load();
        var root = RepositoryRoot.Find();
        var products = ProductCatalogue.Read(Path.Combine(root, "seed", "products.sample.json"));
        var colours = ColourLexicon.Read(Path.Combine(root, "seed", "attributes.sample.json"));

        var worst = products
            .Select(product => (product.ItemId, Windows: tokenizer.WindowsNeeded(PromptTemplate.Positive(product, colours))))
            .OrderByDescending(entry => entry.Windows)
            .First();

        Assert.True(worst.Windows <= 2, $"{worst.ItemId} needs {worst.Windows} windows.");
    }

    [Fact]
    public void A_prompt_that_fits_needs_exactly_one_window()
    {
        var tokenizer = Load();

        Assert.Equal(1, tokenizer.WindowsNeeded("an olive backpack"));
        Assert.Equal(1, tokenizer.WindowsNeeded(string.Empty));
    }

    [Fact]
    public void A_long_prompt_is_cut_into_windows_instead_of_being_thrown_away()
    {
        var tokenizer = Load();

        // Exactly one window of one word, then a different word, so the tail is
        // identifiable rather than a repeat of the head.
        var overflowing = string.Join(" ", Enumerable.Repeat("olive", ClipTokenizer.ContextLength - 2)) + " navy";

        Assert.Equal(2, tokenizer.WindowsNeeded(overflowing));

        var windows = tokenizer.EncodeWindows(overflowing, 2);

        Assert.Equal(2, windows.Length);
        Assert.All(windows, window => Assert.Equal(ClipTokenizer.ContextLength, window.Length));

        // **The tail survives**, which is the whole point: under truncation the
        // word after the seventy-fifth was discarded without a word of warning.
        Assert.Equal(tokenizer.IdOf("olive</w>"), windows[0][1]);
        Assert.Equal(tokenizer.IdOf("navy</w>"), windows[1][1]);
    }

    /// <summary>
    /// Both rows of the guidance batch have to be the same length, so a short
    /// negative prompt beside a long positive one is padded with a window that
    /// carries nothing but its markers.
    /// </summary>
    [Fact]
    public void Asking_for_more_windows_than_the_text_needs_pads_with_empty_ones()
    {
        var tokenizer = Load();

        var windows = tokenizer.EncodeWindows("an olive backpack", 2);

        Assert.Equal(2, windows.Length);
        Assert.Equal(tokenizer.IdOf("<|startoftext|>"), windows[1][0]);
        Assert.Equal(tokenizer.IdOf("<|endoftext|>"), windows[1][1]);
    }
}
