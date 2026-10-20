using ElGuerre.Tendero.Search.Images;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

public sealed class Owlv2ObjectDetectorTests
{
    /// <summary>OWLv2 draws its boxes as a centre and a size, in parts of the padded square: back to the picture's pixels.</summary>
    [Fact]
    public void A_box_is_the_centre_and_size_of_the_padded_square_in_pixels()
    {
        Assert.Equal(new PictureBox(400, 200, 200, 100), Owlv2ObjectDetector.Box(0.5f, 0.25f, 0.2f, 0.1f, 1000));
    }

    /// <summary>A kind asked as several alternatives is as likely as its likeliest one, at the patch that one is likeliest.</summary>
    [Fact]
    public void A_kind_takes_the_best_patch_of_any_of_its_alternatives()
    {
        // Three patches by three queries, row by row: the kind is queries 0 and 2.
        float[] logits =
        [
            -3f, 9f, -1f,
            -2f, 0f, 4f,
            1f, 0f, -5f,
        ];

        Assert.Equal((Patch: 1, Logit: 4f), Owlv2ObjectDetector.Likeliest(logits, 3, [0, 2]));
        Assert.Equal((Patch: 0, Logit: 9f), Owlv2ObjectDetector.Likeliest(logits, 3, [1]));
    }

    /// <summary>Each pixel keeps its own colour on its way into the square: red on the left stays left, blue on the right stays right.</summary>
    [Fact]
    public void Each_pixel_lands_where_it_was()
    {
        byte[] redThenBlue = [255, 0, 0, 0, 0, 255];

        var tensor = Owlv2ObjectDetector.Pixels(redThenBlue, 2, 1);

        var last = Owlv2ObjectDetector.Side - 1;
        // Red and nothing of blue on the left; blue and nothing of red on the right.
        Assert.Equal((1f - 0.48145466f) / 0.26862954f, tensor[0, 0, 0, 0], 2);
        Assert.Equal((0f - 0.40821073f) / 0.27577711f, tensor[0, 2, 0, 0], 2);
        Assert.Equal((1f - 0.40821073f) / 0.27577711f, tensor[0, 2, 0, last], 2);
        Assert.Equal((0f - 0.48145466f) / 0.26862954f, tensor[0, 0, 0, last], 2);
    }

    /// <summary>The picture is laid at the top left of a grey square, as the maker's processor pads it, and normalised with CLIP's mean and deviation.</summary>
    [Fact]
    public void The_picture_is_padded_grey_below_and_normalised()
    {
        var white = Enumerable.Repeat((byte)255, 4 * 2 * 3).ToArray();

        var tensor = Owlv2ObjectDetector.Pixels(white, 4, 2);

        Assert.Equal([1, 3, Owlv2ObjectDetector.Side, Owlv2ObjectDetector.Side], tensor.Dimensions.ToArray());
        Assert.Equal((1f - 0.48145466f) / 0.26862954f, tensor[0, 0, 0, 0], 3);
        Assert.Equal((1f - 0.48145466f) / 0.26862954f, tensor[0, 0, 0, Owlv2ObjectDetector.Side - 1], 3);
        Assert.Equal((128 / 255f - 0.40821073f) / 0.27577711f, tensor[0, 2, Owlv2ObjectDetector.Side - 1, 0], 3);
    }

    /// <summary>
    /// The export, held end to end to the maker's own detections: our tokenizer,
    /// both towers and our resizing against PyTorch and its processor, on four seed
    /// pictures. Skia's resize is near the maker's anti-aliased one and not the
    /// same, so the score is held within a few hundredths and the box by overlap.
    /// </summary>
    [Fact]
    [Trait("Category", "Model")]
    public async Task Each_picture_is_found_as_the_maker_finds_it()
    {
        using var detector = new Owlv2ObjectDetector(Owlv2TextTokenizerTests.Folder());
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root!.FullName, "Tendero.slnx")))
            root = root.Parent;

        foreach (var picture in Owlv2TextTokenizerTests.Reference.Detections)
        {
            var kinds = picture.Found.Select(found => new ObjectKind(found.Words, found.Words, Sold: true)).ToList();
            var bytes = await File.ReadAllBytesAsync(Path.Combine(root.FullName, picture.File), TestContext.Current.CancellationToken);
            var detected = await detector.DetectAsync(bytes, kinds, TestContext.Current.CancellationToken);

            foreach (var expected in picture.Found)
            {
                var ours = detected.Single(found => found.Kind.Code == expected.Words);
                var theirs = Owlv2ObjectDetector.Box((float)expected.Box[0], (float)expected.Box[1], (float)expected.Box[2], (float)expected.Box[3], Math.Max(picture.Width, picture.Height))
                    .ClippedTo(picture.Width, picture.Height)!.Value;
                Assert.True(Math.Abs(ours.Score - Sigmoid(expected.Logit)) < 0.05, $"{picture.File}, {expected.Words}: {ours.Score:0.000} against the maker's {Sigmoid(expected.Logit):0.000}");
                if (Sigmoid(expected.Logit) > 0.3)
                    Assert.True(Overlap(ours.Box, theirs) > 0.8, $"{picture.File}, {expected.Words}: {ours.Box} against the maker's {theirs}");
            }
        }
    }

    private static double Sigmoid(double logit) => 1 / (1 + Math.Exp(-logit));

    private static double Overlap(PictureBox one, PictureBox another)
    {
        var width = Math.Min(one.Left + one.Width, another.Left + another.Width) - Math.Max(one.Left, another.Left);
        var height = Math.Min(one.Top + one.Height, another.Top + another.Height) - Math.Max(one.Top, another.Top);
        var common = Math.Max(0, width) * (double)Math.Max(0, height);
        return common / ((one.Width * (double)one.Height) + (another.Width * (double)another.Height) - common);
    }
}

/// <summary>OWLv2 over the real export, held to the contract; one instance for the class, since each loads 615 MB of networks. It skips where the export is not.</summary>
[Trait("Category", "Model")]
public sealed class Owlv2ObjectDetectorContractTests : ObjectDetectorContractTests
{
    private static readonly Lazy<Owlv2ObjectDetector> Shared = new(() => new Owlv2ObjectDetector(Owlv2TextTokenizerTests.Folder()));

    protected override IObjectDetector Detector() => Shared.Value;
}
