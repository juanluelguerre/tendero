using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.SearchByImage;
using ElGuerre.Tendero.Search.Features.SearchPicturesByText;
using ElGuerre.Tendero.Search.Images;
using FluentValidation;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

/// <summary>
/// The two visual questions (phase 13a, I-6), with no model and no store: what
/// is taken at the door, what comes back and in what order, and what each says
/// when it cannot answer.
/// </summary>
public sealed class VisualSearchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- The picture at the door ----------

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, "jpeg")]
    [InlineData(SKEncodedImageFormat.Png, "png")]
    [InlineData(SKEncodedImageFormat.Webp, "webp")]
    public void A_photograph_a_screenshot_and_a_browsers_picture_are_taken_by_what_their_bytes_are(SKEncodedImageFormat format, string named)
    {
        var (picture, refusal) = UploadedPicture.Read(APicture(120, 80, format));

        Assert.Null(refusal);
        Assert.Equal(new UploadedPicture(120, 80, named), picture);
    }

    [Fact]
    public void Nothing_and_something_that_is_not_a_picture_are_refused()
    {
        Assert.Contains("empty", UploadedPicture.Read([]).Refusal, StringComparison.Ordinal);
        Assert.Contains("JPEG, a PNG or a WebP", UploadedPicture.Read("<svg onload='alert(1)'/>"u8).Refusal, StringComparison.Ordinal);
    }

    /// <summary>The decoder reads a GIF perfectly well. It is refused because of what it is, not because it cannot be read.</summary>
    [Fact]
    public void A_picture_that_moves_is_refused_although_it_could_be_decoded()
    {
        // The smallest GIF there is: one white pixel.
        byte[] gif =
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00,
            0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3B,
        ];

        var (picture, refusal) = UploadedPicture.Read(gif);

        Assert.Null(picture);
        Assert.Contains("JPEG, a PNG or a WebP", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_picture_larger_than_a_side_allows_is_refused_and_one_at_the_bound_is_not()
    {
        var (_, refusal) = UploadedPicture.Read(APicture(UploadedPicture.MaxSide + 1, 2, SKEncodedImageFormat.Png));

        Assert.Contains($"{UploadedPicture.MaxSide + 1} by 2", refusal, StringComparison.Ordinal);
        Assert.NotNull(UploadedPicture.Read(APicture(UploadedPicture.MaxSide, 2, SKEncodedImageFormat.Png)).Picture);
    }

    [Fact]
    public void A_picture_heavier_than_the_bound_is_refused_without_being_read()
    {
        var (picture, refusal) = UploadedPicture.Read(new byte[UploadedPicture.MaxBytes + 1]);

        Assert.Null(picture);
        Assert.Contains("at most 8 MB", refusal, StringComparison.Ordinal);

        // Exactly the bound is not too heavy: these bytes are refused for what they are.
        Assert.DoesNotContain("at most", UploadedPicture.Read(new byte[UploadedPicture.MaxBytes]).Refusal, StringComparison.Ordinal);
    }

    /// <summary>Tall is bounded as wide is, and the bound itself is taken.</summary>
    [Fact]
    public void A_picture_taller_than_a_side_allows_is_refused_and_one_at_the_bound_is_not()
    {
        var (_, refusal) = UploadedPicture.Read(APicture(2, UploadedPicture.MaxSide + 1, SKEncodedImageFormat.Png));

        Assert.Contains($"2 by {UploadedPicture.MaxSide + 1}", refusal, StringComparison.Ordinal);
        Assert.NotNull(UploadedPicture.Read(APicture(2, UploadedPicture.MaxSide, SKEncodedImageFormat.Png)).Picture);
    }

    [Fact]
    public void The_door_names_the_picture_the_limit_and_the_culture()
    {
        var validator = new SearchByImageValidator();
        var picture = APicture(40, 40, SKEncodedImageFormat.Png);

        Assert.True(validator.Validate(new SearchByImageQuery(picture, "es", 12)).IsValid);
        Assert.Equal("picture", Assert.Single(validator.Validate(new SearchByImageQuery("not a picture"u8.ToArray(), "es", 12)).Errors).PropertyName);
        Assert.Equal("limit", Assert.Single(validator.Validate(new SearchByImageQuery(picture, "es", VisualSearch.MaxLimit + 1)).Errors).PropertyName);
        Assert.Equal("limit", Assert.Single(validator.Validate(new SearchByImageQuery(picture, "es", 0)).Errors).PropertyName);
        Assert.False(validator.Validate(new SearchByImageQuery(picture, "fr", 12)).IsValid);
    }

    [Theory]
    [InlineData("mesa redonda", true)]
    [InlineData("", false)]
    [InlineData("m", false)]
    public void Words_against_the_pictures_need_at_least_two_characters(string q, bool valid)
    {
        Assert.Equal(valid, new SearchPicturesByTextValidator().Validate(new SearchPicturesByTextQuery(q, "es", 12)).IsValid);
    }

    // ---------- The answer ----------

    [Fact]
    public async Task The_nearest_come_back_as_the_shops_cards_in_the_pictures_order_with_the_picture_that_matched()
    {
        var pictures = new Nearest(new("lamp", "img-lamp-brass", 0.91), new("stool", "img-stool", 0.74));
        var search = new VisualSearch(pictures, new Index("stool", "lamp"));

        var result = await search.NearestAsync(new float[4], floor: 0, "es", limit: 5, Ct);

        Assert.Equal(["lamp", "stool"], result.Items.Select(item => item.Product.ProductId));
        Assert.Equal(("img-lamp-brass", 0.91), (result.Items[0].Picture, result.Items[0].Similarity));
        Assert.Equal(5, pictures.AskedFor);
    }

    /// <summary>A nearest neighbour always exists; a photograph of a cat must not find twelve products.</summary>
    [Fact]
    public async Task What_is_not_near_enough_is_not_an_answer()
    {
        var search = new VisualSearch(new Nearest(new("lamp", "a", 0.80), new("stool", "b", 0.60), new("rug", "c", 0.59)), new Index("lamp", "stool", "rug"));

        var result = await search.NearestAsync(new float[4], floor: 0.60, "es", limit: 5, Ct);

        Assert.Equal(["lamp", "stool"], result.Items.Select(item => item.Product.ProductId));
    }

    [Fact]
    public async Task Nothing_near_enough_is_an_empty_answer_and_the_index_is_not_asked()
    {
        var index = new Index("lamp");

        var result = await new VisualSearch(new Nearest(new PictureMatch("lamp", "a", 0.2)), index).NearestAsync(new float[4], floor: 0.5, "es", 5, Ct);

        Assert.Empty(result.Items);
        Assert.Equal(0, index.Asked);
    }

    /// <summary>Withdrawn between its picture being embedded and now: left out, not an error.</summary>
    [Fact]
    public async Task A_product_the_index_no_longer_holds_is_left_out()
    {
        var search = new VisualSearch(new Nearest(new("gone", "a", 0.9), new("lamp", "b", 0.8)), new Index("lamp"));

        var result = await search.NearestAsync(new float[4], floor: 0, "es", 5, Ct);

        Assert.Equal("lamp", Assert.Single(result.Items).Product.ProductId);
    }

    // ---------- The two questions ----------

    [Fact]
    public async Task A_picture_is_embedded_as_a_picture_and_answered_above_the_pictures_floor()
    {
        var model = new Model();
        var handler = new SearchByImageHandler(model, Search(new("lamp", "a", 0.70), new("stool", "b", 0.50)), Floors(picture: 0.6, text: 0.9));

        var result = await handler.HandleAsync(new SearchByImageQuery(APicture(40, 40, SKEncodedImageFormat.Png), "es", 12), Ct);

        Assert.Equal("lamp", Assert.Single(result.Items).Product.ProductId);
        Assert.Equal((1, 0), (model.Pictures, model.Texts));
    }

    [Fact]
    public async Task Words_are_embedded_as_text_and_answered_above_the_texts_floor()
    {
        var model = new Model();
        var handler = new SearchPicturesByTextHandler(model, Search(new("lamp", "a", 0.12), new("stool", "b", 0.04)), Floors(picture: 0.9, text: 0.1));

        var result = await handler.HandleAsync(new SearchPicturesByTextQuery("lámpara de latón", "es", 12), Ct);

        Assert.Equal("lamp", Assert.Single(result.Items).Product.ProductId);
        Assert.Equal((0, 1), (model.Pictures, model.Texts));
        Assert.Equal("lámpara de latón", model.LastText);
    }

    /// <summary>A header that reads as a picture over a body that does not decode: the caller's mistake, said as one.</summary>
    [Fact]
    public async Task A_picture_the_model_cannot_decode_is_the_callers_mistake_and_names_the_picture()
    {
        var handler = new SearchByImageHandler(new Model { Failure = new ArgumentException("truncated") }, Search(), Floors());

        var refusal = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.HandleAsync(new SearchByImageQuery(APicture(40, 40, SKEncodedImageFormat.Png), "es", 12), Ct));

        Assert.Equal("picture", Assert.Single(refusal.Errors).PropertyName);
    }

    /// <summary>The model's own failure — a file missing from its folder — is the stage being down, not a 500.</summary>
    [Fact]
    public async Task A_model_that_fails_is_the_stage_unavailable_for_both_questions()
    {
        var model = new Model { Failure = new FileNotFoundException("vision.onnx") };

        await Assert.ThrowsAsync<SearchUnavailableException>(() =>
            new SearchByImageHandler(model, Search(), Floors()).HandleAsync(new SearchByImageQuery(APicture(40, 40, SKEncodedImageFormat.Png), "es", 12), Ct));
        await Assert.ThrowsAsync<SearchUnavailableException>(() =>
            new SearchPicturesByTextHandler(model, Search(), Floors()).HandleAsync(new SearchPicturesByTextQuery("lamp", "en", 12), Ct));
    }

    /// <summary>"Never had it" is not "it is down": the first is said as what it is, so nothing counts it as a failure.</summary>
    [Fact]
    public async Task A_process_without_the_model_says_the_stage_is_not_there()
    {
        var none = new NoImageEmbeddings();

        await Assert.ThrowsAsync<SearchStageNotConfiguredException>(() =>
            new SearchByImageHandler(none, Search(), Floors()).HandleAsync(new SearchByImageQuery(APicture(40, 40, SKEncodedImageFormat.Png), "es", 12), Ct));
        await Assert.ThrowsAsync<SearchStageNotConfiguredException>(() =>
            new SearchPicturesByTextHandler(none, Search(), Floors()).HandleAsync(new SearchPicturesByTextQuery("lamp", "en", 12), Ct));
    }

    [Fact]
    public async Task Cancellation_is_not_mistaken_for_a_model_that_failed()
    {
        var model = new Model { Failure = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new SearchPicturesByTextHandler(model, Search(), Floors()).HandleAsync(new SearchPicturesByTextQuery("lamp", "en", 12), Ct));
    }

    /// <summary>
    /// Measured: every logical processor is the slowest setting there is (695 ms a
    /// picture against 121 ms with eight threads). The rule is held here; that a
    /// session is given it is not something a test without the model can see.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(12, 6)]
    [InlineData(16, 8)]
    [InlineData(64, 8)]
    public void One_inference_takes_half_the_processors_and_never_more_than_eight(int processors, int threads)
    {
        Assert.Equal(threads, SiglipOnnxEmbeddings.ThreadsFor(processors));
        Assert.Equal(SiglipOnnxEmbeddings.ThreadsFor(Environment.ProcessorCount), SiglipOnnxEmbeddings.Threads);
    }

    private static VisualSearch Search(params PictureMatch[] near) =>
        new(new Nearest(near), new Index([.. near.Select(match => match.ProductId)]));

    private static IOptions<VisualSearchOptions> Floors(double picture = 0, double text = 0) =>
        Options.Create(new VisualSearchOptions { MinPictureSimilarity = picture, MinTextSimilarity = text });

    private static byte[] APicture(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        return encoded.ToArray();
    }

    private sealed class Nearest(params PictureMatch[] near) : IPictureSearch
    {
        public int AskedFor { get; private set; }

        public Task<IReadOnlyList<PictureMatch>> NearestAsync(ReadOnlyMemory<float> vector, int limit, CancellationToken ct = default)
        {
            AskedFor = limit;
            return Task.FromResult<IReadOnlyList<PictureMatch>>(near);
        }
    }

    private sealed class Index(params string[] held) : IProductProfileReader
    {
        public int Asked { get; private set; }

        public Task<IReadOnlyList<ProductProfile>> ByIdsAsync(IReadOnlyCollection<string> productIds, string culture, CancellationToken ct = default)
        {
            Asked++;
            return Task.FromResult<IReadOnlyList<ProductProfile>>(
                [.. held.Where(productIds.Contains).Select(id => new ProductProfile(Strategies.Hit(id), null, []))]);
        }

        public Task<IReadOnlyList<ProductProfile>> ByCodesAsync(IReadOnlyCollection<string> codes, string culture, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProductProfile>> InCategoriesAsync(IReadOnlyCollection<string> categories, string culture, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class Model : IImageEmbeddingGenerator
    {
        public EmbeddingModelDescriptor Model_ => EmbeddingModelDescriptor.Siglip2Base;

        EmbeddingModelDescriptor IImageEmbeddingGenerator.Model => Model_;

        public Exception? Failure { get; init; }
        public int Pictures { get; private set; }
        public int Texts { get; private set; }
        public string? LastText { get; private set; }

        public ValueTask<ReadOnlyMemory<float>> EmbedPictureAsync(ReadOnlyMemory<byte> picture, CancellationToken ct = default)
        {
            Pictures++;
            return Failure is not null ? throw Failure : ValueTask.FromResult<ReadOnlyMemory<float>>(new float[4]);
        }

        public ValueTask<ReadOnlyMemory<float>> EmbedTextAsync(string text, CancellationToken ct = default)
        {
            Texts++;
            LastText = text;
            return Failure is not null ? throw Failure : ValueTask.FromResult<ReadOnlyMemory<float>>(new float[4]);
        }
    }
}
