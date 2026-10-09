using System.Text.Json;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

/// <summary>
/// SigLIP 2 in process, held to the maker's own embeddings (phase 13a, P13-2):
/// the same pictures and the same texts through PyTorch — written down by the
/// export, <c>src/AppHost/scripts/prepare-siglip.py</c>, and committed — and
/// through this adapter, compared.
///
/// A text is the export's own business and agrees to the fifth decimal. A
/// picture also passes through our resize and the maker's, which are two
/// libraries' bilinear and not the same pixels: how near they end is the number
/// asserted here, and it was measured before it was written.
///
/// It needs the export, 1.5 GB that are not the repository's: where it is
/// missing these say how to make it and skip.
/// </summary>
public sealed class SiglipOnnxEmbeddingsTests
{
    private const string Variable = "TENDERO_SIGLIP_MODELS";

    private sealed record Fixture(string Model, string Revision, int Dimensions, IReadOnlyList<Picture> Pictures, IReadOnlyList<Text> Texts);

    private sealed record Picture(string File, float[] Embedding);

    private sealed record Text(string text, float[] Embedding)
    {
        public string Words => text;
    }

    private static readonly Fixture Reference = JsonSerializer.Deserialize<Fixture>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Images", "siglip-embedding-fixture.json")), JsonSerializerOptions.Web)!;

    private static string Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Tendero.slnx")))
            root = root.Parent;
        return root!.FullName;
    }

    private static SiglipOnnxEmbeddings Load()
    {
        var folder = Environment.GetEnvironmentVariable(Variable) ?? Path.Combine(Root(), "models", "siglip2");
        if (!File.Exists(Path.Combine(folder, SiglipOnnxEmbeddings.VisionFile)))
            Assert.Skip($"No SigLIP 2 export at '{folder}'. Run src/AppHost/scripts/prepare-siglip.py (its header says how), or set {Variable}.");

        return new SiglipOnnxEmbeddings(folder);
    }

    private static float Cosine(ReadOnlySpan<float> one, ReadOnlySpan<float> another)
    {
        var dot = 0f;
        for (var at = 0; at < one.Length; at++)
            dot += one[at] * another[at];
        return dot;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_reference_is_of_the_model_the_shop_pins()
    {
        var model = EmbeddingModelDescriptor.Siglip2Base;

        Assert.Equal((model.ModelId, model.Revision, model.Dimensions), (Reference.Model, Reference.Revision, Reference.Dimensions));
        Assert.Equal((SiglipTextTokenizer.Model, SiglipTextTokenizer.Revision), (model.ModelId, model.Revision));
    }

    /// <summary>Words already in lower case reach the tower as the maker's tokenizer gives them: the embedding is the maker's.</summary>
    [Fact]
    public async Task A_text_in_lower_case_embeds_as_the_makers_does()
    {
        using var siglip = Load();

        foreach (var expected in Reference.Texts.Where(text => text.Words == text.Words.ToLowerInvariant()))
        {
            var actual = await siglip.EmbedTextAsync(expected.Words, Ct);

            Assert.Equal(Reference.Dimensions, actual.Length);
            Assert.True(Cosine(actual.Span, expected.Embedding) > 0.9999f, $"\"{expected.Words}\": cosine {Cosine(actual.Span, expected.Embedding):F6}");
        }
    }

    /// <summary>
    /// **A text is lower-cased before it is tokenized**, which the maker's
    /// tokenizer does not do and the model's configuration asks for. Measured by
    /// the export, each seed product's name against every seed picture: Spanish
    /// names find their own picture first 88 % of the time lower-cased and 65 %
    /// as written, and in capitals 1 %. So *ZAPATILLAS* embeds as *zapatillas*
    /// does — and not as the maker's own tokenizer would have it.
    /// </summary>
    [Fact]
    public async Task A_text_in_capitals_embeds_as_its_lower_case_and_not_as_the_makers_tokenizer_would_have_it()
    {
        using var siglip = Load();
        var asTheMakerHasIt = Reference.Texts.Single(text => text.Words == "ZAPATILLAS").Embedding;

        var (capitals, lower) = (await siglip.EmbedTextAsync("ZAPATILLAS", Ct), await siglip.EmbedTextAsync("zapatillas", Ct));

        Assert.True(Cosine(capitals.Span, lower.Span) > 0.9999f);
        Assert.True(Cosine(capitals.Span, asTheMakerHasIt) < 0.9f, $"cosine with the maker's capitals {Cosine(capitals.Span, asTheMakerHasIt):F4}");
    }

    /// <summary>
    /// The seed's own pictures through our decoding and our resize, against the
    /// maker's preprocessing: 0.999998 at the least on these four, measured. With
    /// a graphics library's own *linear* the worst was 0.983 — the same picture
    /// through a different triangle — which is why the resize is Pillow's
    /// arithmetic written out, and why this is held four nines high.
    /// </summary>
    [Fact]
    public async Task A_picture_embeds_as_the_makers_does()
    {
        using var siglip = Load();

        List<(string File, float Cosine)> measured = [];
        foreach (var expected in Reference.Pictures)
        {
            var actual = await siglip.EmbedPictureAsync(await File.ReadAllBytesAsync(Path.Combine(Root(), expected.File), Ct), Ct);

            Assert.Equal(Reference.Dimensions, actual.Length);
            measured.Add((expected.File, Cosine(actual.Span, expected.Embedding)));
        }

        Assert.True(
            measured.All(picture => picture.Cosine > Agreement),
            string.Join("; ", measured.Select(picture => $"{Path.GetFileName(picture.File)} {picture.Cosine:F6}")));
    }

    private const float Agreement = 0.9999f;

    /// <summary>What the port promises whoever stores a vector: unit length, so the nearest is the largest dot product.</summary>
    [Fact]
    public async Task Every_vector_is_unit_length()
    {
        using var siglip = Load();

        var text = await siglip.EmbedTextAsync("una planta de interior", Ct);
        var picture = await siglip.EmbedPictureAsync(await File.ReadAllBytesAsync(Path.Combine(Root(), Reference.Pictures[0].File), Ct), Ct);

        Assert.Equal(1f, Cosine(text.Span, text.Span), 4);
        Assert.Equal(1f, Cosine(picture.Span, picture.Span), 4);
    }

    /// <summary>What it is all for: words find their picture. Each text is nearer the picture it describes than any other of the four, in both languages.</summary>
    [Fact]
    public async Task Words_are_nearest_the_picture_they_describe()
    {
        using var siglip = Load();
        var pictures = new Dictionary<string, ReadOnlyMemory<float>>();
        foreach (var picture in Reference.Pictures)
            pictures[Path.GetFileNameWithoutExtension(picture.File)] = await siglip.EmbedPictureAsync(await File.ReadAllBytesAsync(Path.Combine(Root(), picture.File), Ct), Ct);

        foreach (var (words, wanted) in new[]
                 {
                     ("running shoes", "B073WXYZ01"), ("a green bike pannier", "B13BAG0711"),
                     ("una planta de interior", "B16PLA1401"), ("Sofá gris de dos plazas", "B16FUR1303"),
                 })
        {
            var text = await siglip.EmbedTextAsync(words, Ct);
            var nearest = pictures.MaxBy(picture => Cosine(text.Span, picture.Value.Span)).Key;

            Assert.Equal(wanted, nearest);
        }
    }

    [Fact]
    public async Task Bytes_that_are_not_a_picture_are_refused_as_an_argument()
    {
        using var siglip = Load();

        await Assert.ThrowsAsync<ArgumentException>(async () => await siglip.EmbedPictureAsync("not a picture"u8.ToArray(), Ct));
    }

    /// <summary>A folder without the export is said to be one, with what to run — not a null reference at the first search.</summary>
    [Fact]
    public void A_folder_without_the_export_is_refused_with_what_to_run()
    {
        var empty = Directory.CreateTempSubdirectory("tendero-no-siglip-");
        try
        {
            var refusal = Assert.Throws<FileNotFoundException>(() => new SiglipOnnxEmbeddings(empty.FullName));
            Assert.Contains("prepare-siglip.py", refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            empty.Delete();
        }
    }

    /// <summary>With no model the port says the stage is not there — the reranker's own answer — and the shop goes on without visual search.</summary>
    [Fact]
    public async Task With_no_model_the_port_says_the_stage_is_not_configured()
    {
        IImageEmbeddingGenerator none = new NoImageEmbeddings();

        Assert.False(none.IsConfigured);
        Assert.Equal(EmbeddingModelDescriptor.Siglip2Base, none.Model);
        var missing = await Assert.ThrowsAsync<SearchStageNotConfiguredException>(async () => await none.EmbedTextAsync("zapatillas", Ct));
        Assert.Equal(NoImageEmbeddings.Stage, missing.Stage);
        await Assert.ThrowsAsync<SearchStageNotConfiguredException>(async () => await none.EmbedPictureAsync(new byte[1], Ct));
    }
}
