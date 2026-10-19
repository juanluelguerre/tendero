using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using SkiaSharp;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

/// <summary>
/// The grid detector with no model (the camera search spec, CS-2): its cells, its
/// probability, and that each kind takes the cell most like its words — held by
/// an embedder that sees only a cell's mean colour, so the arithmetic is the
/// test's own.
/// </summary>
public sealed class SiglipGridObjectDetectorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_cells_are_the_whole_then_halves_then_thirds_all_inside_the_picture()
    {
        var cells = SiglipGridObjectDetector.Cells(320, 240);

        Assert.Equal(1 + 9 + 25, cells.Count);
        Assert.Equal(new PictureBox(0, 0, 320, 240), cells[0]);
        Assert.Equal(new PictureBox(0, 0, 160, 120), cells[1]);
        Assert.Equal(new PictureBox(160, 120, 160, 120), cells[9]);
        Assert.All(cells, cell => Assert.Equal(cell, cell.ClippedTo(320, 240)));
    }

    /// <summary>SigLIP's own line between yes and no: where the learned scale and bias cancel, a cell is as likely as not.</summary>
    [Fact]
    public void The_probability_is_one_half_where_scale_and_bias_cancel_and_grows_with_the_cosine()
    {
        var even = -SiglipGridObjectDetector.Bias / SiglipGridObjectDetector.Scale;

        Assert.Equal(0.5, SiglipGridObjectDetector.Probability(even), 6);
        Assert.True(SiglipGridObjectDetector.Probability(even + 0.05) > 0.99);
        Assert.True(SiglipGridObjectDetector.Probability(even - 0.05) < 0.01);
    }

    [Fact]
    public async Task Each_kind_takes_the_cell_most_like_its_words()
    {
        var detector = new SiglipGridObjectDetector(new MeanColour());
        var red = new ObjectKind("RED", "red", Sold: true);

        var found = await detector.DetectAsync(RedCornerOnWhite(), [red], Ct);

        Assert.Equal(new PictureBox(0, 0, 106, 80), Assert.Single(found).Box);
    }

    [Fact]
    public async Task A_kind_of_several_alternatives_takes_the_one_that_matches_best()
    {
        var detector = new SiglipGridObjectDetector(new MeanColour());
        var either = new ObjectKind("EITHER", "green or red", Sold: true);

        var found = await detector.DetectAsync(RedCornerOnWhite(), [either], Ct);

        Assert.Equal(new PictureBox(0, 0, 106, 80), Assert.Single(found).Box);
    }

    [Fact]
    public void It_asks_with_the_sentence_SigLIP_was_evaluated_with() =>
        Assert.Equal("This is a photo of a desk lamp.", SiglipGridObjectDetector.Asked("desk lamp"));

    /// <summary>White, with its top left third red: exactly one of the grid's cells.</summary>
    private static byte[] RedCornerOnWhite()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(320, 240, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, 106, 80, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    /// <summary>A picture as its mean colour, a word as the colour it names: unit vectors, as the port promises.</summary>
    private sealed class MeanColour : IImageEmbeddingGenerator
    {
        public EmbeddingModelDescriptor Model => EmbeddingModelDescriptor.Siglip2Base;

        public ValueTask<ReadOnlyMemory<float>> EmbedPictureAsync(ReadOnlyMemory<byte> picture, CancellationToken cancellationToken = default)
        {
            using var bitmap = SKBitmap.Decode(picture.ToArray());
            double r = 0, g = 0, b = 0;
            foreach (var pixel in bitmap.Pixels)
            {
                r += pixel.Red;
                g += pixel.Green;
                b += pixel.Blue;
            }

            return ValueTask.FromResult(Unit(r, g, b));
        }

        public ValueTask<ReadOnlyMemory<float>> EmbedTextAsync(string text, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(text == "This is a photo of a red." ? Unit(1, 0, 0) : Unit(0, 1, 0));

        private static ReadOnlyMemory<float> Unit(double r, double g, double b)
        {
            var length = Math.Sqrt((r * r) + (g * g) + (b * b));
            return new[] { (float)(r / length), (float)(g / length), (float)(b / length) };
        }
    }
}

/// <summary>The grid detector over the real SigLIP 2 export, held to the contract; it skips where the export is not.</summary>
[Trait("Category", "Model")]
public sealed class SiglipGridObjectDetectorContractTests : ObjectDetectorContractTests
{
    protected override IObjectDetector? Detector()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Tendero.slnx")))
            root = root.Parent;
        var folder = Environment.GetEnvironmentVariable("TENDERO_SIGLIP_MODELS") ?? Path.Combine(root!.FullName, "models", "siglip2");
        return File.Exists(Path.Combine(folder, SiglipOnnxEmbeddings.VisionFile))
            ? new SiglipGridObjectDetector(new SiglipOnnxEmbeddings(folder))
            : null;
    }
}
