using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// The file the catalogue references, and the checks that make it match the
/// specification instead of nearly matching it.
///
/// All of this runs on synthetic pixels: no GPU, no model, no image on disk.
/// </summary>
public sealed class ImagingTests
{
    /// <summary>A square of background with an optional dark band.</summary>
    private static byte[] Frame(int size, int bandFrom = -1, int bandTo = -1)
    {
        var rgb = new byte[size * size * 3];

        for (var pixel = 0; pixel < size * size; pixel++)
        {
            rgb[(pixel * 3) + 0] = ImageSpec.Background.Red;
            rgb[(pixel * 3) + 1] = ImageSpec.Background.Green;
            rgb[(pixel * 3) + 2] = ImageSpec.Background.Blue;
        }

        for (var row = Math.Max(bandFrom, 0); row <= bandTo && row < size; row++)
            for (var column = 0; column < size; column++)
            {
                var at = ((row * size) + column) * 3;
                rgb[at] = rgb[at + 1] = rgb[at + 2] = 20;
            }

        return rgb;
    }

    [Fact]
    public void The_file_comes_out_at_the_size_the_specification_asks_for()
    {
        var webp = ImageNormaliser.ToWebp(Frame(1024, 300, 700), 1024, out _);

        using var codec = SkiaSharp.SKCodec.Create(new MemoryStream(webp));

        Assert.Equal(ImageSpec.Size, codec.Info.Width);
        Assert.Equal(ImageSpec.Size, codec.Info.Height);
    }

    /// <summary>
    /// **Read from the container, not taken on trust from the encoder.** A
    /// transparent background would depend on the page's theme, and these
    /// generators leave halos at the edges; the specification says no alpha, and
    /// three byte comparisons say whether there is one.
    /// </summary>
    [Fact]
    public void The_file_has_no_alpha_channel_at_all()
    {
        var webp = ImageNormaliser.ToWebp(Frame(1024, 300, 700), 1024, out _);

        Assert.True(
            WebpFacts.IsOpaqueLossyWebp(webp),
            "The container is not a plain lossy WebP; something put an alpha channel in it.");
    }

    [Fact]
    public void A_flat_illustration_fits_the_weight_budget_at_full_quality()
    {
        var webp = ImageNormaliser.ToWebp(Frame(1024, 300, 700), 1024, out var quality);

        Assert.True(webp.Length <= ImageSpec.MaxBytes, $"{webp.Length / 1024} KB");
        Assert.Equal(ImageSpec.Quality, quality);
    }

    [Fact]
    public void The_background_is_the_declared_grey_even_at_the_corners()
    {
        var webp = ImageNormaliser.ToWebp(Frame(1024, 300, 700), 1024, out _);

        using var bitmap = SkiaSharp.SKBitmap.Decode(webp);
        var corner = bitmap.GetPixel(2, 2);

        Assert.InRange(Math.Abs(corner.Red - ImageSpec.Background.Red), 0, 3);
        Assert.InRange(Math.Abs(corner.Green - ImageSpec.Background.Green), 0, 3);
        Assert.InRange(Math.Abs(corner.Blue - ImageSpec.Background.Blue), 0, 3);
    }

    [Fact]
    public void An_object_in_the_middle_passes_the_safe_area()
    {
        var result = SafeAreaProbe.Probe(Frame(1000, 300, 700), 1000);

        Assert.True(result.Passed, result.Detail);
    }

    [Fact]
    public void An_object_touching_the_top_edge_fails_because_the_card_would_crop_it()
    {
        var result = SafeAreaProbe.Probe(Frame(1000, 0, 400), 1000);

        Assert.False(result.Passed, result.Detail);
        Assert.Equal(0, result.FirstRow);
    }

    /// <summary>
    /// **The one a naive implementation passes.** Taking the minimum and maximum
    /// row with ink reports a perfectly centred nothing for a blank frame — and a
    /// blank frame is exactly what a decoder that overflowed in fp16 produces, so
    /// the check that exists to catch bad framing would wave through the worst
    /// failure the pipeline has.
    /// </summary>
    [Fact]
    public void A_blank_frame_fails_loudly_instead_of_passing_for_being_well_centred()
    {
        var result = SafeAreaProbe.Probe(Frame(1000), 1000);

        Assert.False(result.Passed);
        Assert.Contains("blank", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// **The bug the first real image found.** The probe used to measure distance
    /// from the declared #FAF9F7, and the model painted a plain grey that was a
    /// different plain grey — so every pixel counted as ink and a perfectly framed
    /// backpack was reported as filling the whole frame. The question is where the
    /// object is, and the background is whatever the picture's corners say it is.
    /// </summary>
    [Fact]
    public void An_object_on_a_background_the_model_chose_is_still_measured_correctly()
    {
        var size = 1000;
        var rgb = new byte[size * size * 3];

        // Not the declared background: a plain mid grey, like the one SDXL
        // actually produces when asked for "plain light warm grey".
        Array.Fill(rgb, (byte)0xD0);

        for (var row = 300; row <= 700; row++)
            for (var column = 0; column < size; column++)
            {
                var at = ((row * size) + column) * 3;
                rgb[at] = rgb[at + 1] = rgb[at + 2] = 20;
            }

        var result = SafeAreaProbe.Probe(rgb, size);

        Assert.True(result.Passed, result.Detail);
        Assert.Equal(300, result.FirstRow);
        Assert.Equal(700, result.LastRow);
    }

    [Fact]
    public void A_single_stray_pixel_near_the_edge_does_not_fail_the_image()
    {
        var rgb = Frame(1000, 300, 700);
        var at = ((3 * 1000) + 500) * 3;
        rgb[at] = rgb[at + 1] = rgb[at + 2] = 0;

        Assert.True(SafeAreaProbe.Probe(rgb, 1000).Passed);
    }
}
