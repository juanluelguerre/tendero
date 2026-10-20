using ElGuerre.Tendero.Search.Images;
using SkiaSharp;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

/// <summary>
/// What every detector behind <see cref="IObjectDetector"/> owes the search
/// (the camera search spec, CS-2). The detector is measured before it is chosen,
/// so each candidate inherits this before it is measured: it names only the kinds
/// it was given, its boxes lie inside the picture, its scores are probabilities,
/// and what is not a picture is refused rather than guessed at.
/// </summary>
public abstract class ObjectDetectorContractTests
{
    /// <summary>The picture every case asks about: a white field with a red square in it.</summary>
    protected const int Width = 320;
    protected const int Height = 240;

    protected static readonly ObjectKind Lamp = new("DESK_LAMP", "desk lamp", Sold: true);
    protected static readonly ObjectKind Pan = new("COOKWARE", "frying pan", Sold: true);
    protected static readonly ObjectKind Bottle = new("bottle", "bottle", Sold: false);

    /// <summary>The detector, or null when it is not running here (the test then skips with the reason).</summary>
    protected abstract IObjectDetector? Detector();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IObjectDetector Running()
    {
        var detector = Detector();
        Assert.SkipWhen(detector is null, "This detector is not running here.");
        return detector!;
    }

    protected static byte[] Picture()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(80, 60, 120, 100, red);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    [Fact]
    public void Its_name_is_stable_and_lowercase()
    {
        var detector = Running();

        Assert.Matches("^[a-z0-9-]+$", detector.Name);
        Assert.True(detector.IsConfigured);
    }

    [Fact]
    public async Task It_names_only_the_kinds_it_was_given()
    {
        ObjectKind[] asked = [Lamp, Bottle];

        var found = await Running().DetectAsync(Picture(), asked, Ct);

        Assert.All(found, detected => Assert.Contains(detected.Kind, asked));
    }

    [Fact]
    public async Task Its_boxes_lie_inside_the_picture()
    {
        var found = await Running().DetectAsync(Picture(), [Lamp, Pan, Bottle], Ct);

        Assert.All(found, detected =>
        {
            Assert.InRange(detected.Box.Left, 0, Width - 1);
            Assert.InRange(detected.Box.Top, 0, Height - 1);
            Assert.True(detected.Box.Width > 0 && detected.Box.Height > 0, $"An empty box: {detected.Box}.");
            Assert.True(detected.Box.Left + detected.Box.Width <= Width, $"Past the right edge: {detected.Box}.");
            Assert.True(detected.Box.Top + detected.Box.Height <= Height, $"Past the bottom edge: {detected.Box}.");
        });
    }

    [Fact]
    public async Task Its_scores_are_probabilities()
    {
        var found = await Running().DetectAsync(Picture(), [Lamp, Pan, Bottle], Ct);

        Assert.All(found, detected => Assert.InRange(detected.Score, 0.0, 1.0));
    }

    [Fact]
    public async Task Asked_for_no_kind_it_finds_nothing()
    {
        Assert.Empty(await Running().DetectAsync(Picture(), [], Ct));
    }

    [Fact]
    public async Task What_is_not_a_picture_is_refused()
    {
        var detector = Running();

        await Assert.ThrowsAsync<ArgumentException>(() => detector.DetectAsync("not a picture"u8.ToArray(), [Lamp], Ct));
    }

    [Fact]
    public async Task A_cancelled_question_stops()
    {
        var detector = Running();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => detector.DetectAsync(Picture(), [Lamp], cancelled.Token));
    }
}

/// <summary>
/// The scripted detector, held to the contract like any other: the browser specs
/// and the degraded paths run on it, so it has to behave as a real one would —
/// including clipping a scripted box that runs past the picture.
/// </summary>
public sealed class ScriptedObjectDetectorContractTests : ObjectDetectorContractTests
{
    protected override IObjectDetector Detector() => new ScriptedObjectDetector(
        new ScriptedObject(Lamp.Code, new PictureBox(70, 50, 140, 120), 0.9),
        new ScriptedObject(Pan.Code, new PictureBox(250, 200, 200, 200), 0.4),
        new ScriptedObject(Bottle.Code, new PictureBox(10, 10, 30, 60), 0.2));
}
