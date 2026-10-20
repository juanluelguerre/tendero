using ElGuerre.Tendero.Search.Images;
using Microsoft.Extensions.AI;
using SkiaSharp;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

/// <summary>
/// A generative model with eyes, asked for boxes (the camera search spec, CS-2),
/// with no model: what it is sent, and how its answer is read — coordinates in
/// thousandths of the picture, kinds it was not given dropped, a box past the
/// edge clipped, and an answer that is not a list taken as nothing found.
/// </summary>
public sealed class VisionModelObjectDetectorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ObjectKind Lamp = new("DESK_LAMP", "desk lamp", Sold: true);
    private static readonly ObjectKind Bottle = new("bottle", "bottle", Sold: false);

    private static byte[] Picture(int width = 400, int height = 200)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    [Fact]
    public async Task Its_thousandths_are_the_pictures_pixels()
    {
        var detector = new VisionModelObjectDetector(new Answering("""[{"kind": "DESK_LAMP", "box": [100, 250, 500, 750]}]"""));

        var found = Assert.Single(await detector.DetectAsync(Picture(400, 200), [Lamp], Ct));

        Assert.Equal((Lamp, new PictureBox(40, 50, 160, 100)), (found.Kind, found.Box));
    }

    [Fact]
    public async Task A_kind_it_was_not_given_is_dropped_and_a_box_past_the_edge_is_clipped()
    {
        var detector = new VisionModelObjectDetector(new Answering("""
            Here they are:
            [{"kind": "TELEVISION", "box": [0, 0, 500, 500]}, {"kind": "bottle", "box": [900, 900, 1200, 1100]}]
            """));

        var found = Assert.Single(await detector.DetectAsync(Picture(400, 200), [Lamp, Bottle], Ct));

        Assert.Equal((Bottle, new PictureBox(360, 180, 40, 20)), (found.Kind, found.Box));
    }

    /// <summary>
    /// Qwen's grounding answers in the format it was trained on, whatever the
    /// question asked for — <c>bbox_2d</c> and <c>label</c> — and on a busy picture it
    /// can repeat itself until it runs out of tokens. Seen on the author's desk
    /// photograph (2026-10-19): every entry before the cut is an answer.
    /// </summary>
    [Fact]
    public async Task Its_own_format_is_read_and_a_list_cut_short_keeps_its_whole_entries()
    {
        var detector = new VisionModelObjectDetector(new Answering("""
            ```json
            [
              {"bbox_2d": [100, 250, 500, 750], "label": "DESK_LAMP"},
              {"bbox_2d": [0, 0, 100, 100], "label": "bottle"},
              {"bbox_2d": [0, 0, 100, 100], "label": "bottle"},
              {"bbox_2d": [0, 0, 10
            """));

        var found = await detector.DetectAsync(Picture(400, 200), [Lamp, Bottle], Ct);

        Assert.Equal([(Lamp, new PictureBox(40, 50, 160, 100)), (Bottle, new PictureBox(0, 0, 40, 20))], found.Select(f => (f.Kind, f.Box)));
    }

    /// <summary>Asked in its own format, the model labels a box with the kind's words as often as with its code (2026-10-19, ABO's run).</summary>
    [Theory]
    [InlineData("desk lamp")]
    [InlineData("Desk Lamp")]
    [InlineData("desk_lamp")]
    [InlineData("DESK_LAMP: desk lamp")]
    public async Task A_kind_is_known_by_its_words_as_well_as_its_code(string label)
    {
        var detector = new VisionModelObjectDetector(new Answering($$"""[{"bbox_2d": [0, 0, 500, 500], "label": "{{label}}"}]"""));

        Assert.Equal(Lamp, Assert.Single(await detector.DetectAsync(Picture(), [Lamp, Bottle], Ct)).Kind);
    }

    [Theory]
    [InlineData("I cannot see any of those.")]
    [InlineData("[{\"kind\": \"DESK_LAMP\", \"box\": [1, 2]}]")]
    [InlineData("[{\"kind\": \"DESK_LAMP\", \"box\": [100, 200, 300")]
    public async Task An_answer_that_is_not_a_list_of_boxes_is_nothing_found(string answer)
    {
        var detector = new VisionModelObjectDetector(new Answering(answer));

        Assert.Empty(await detector.DetectAsync(Picture(), [Lamp], Ct));
    }

    [Fact]
    public async Task It_sends_the_picture_and_names_every_kind_by_its_code_and_words()
    {
        var model = new Answering("[]");

        await new VisionModelObjectDetector(model).DetectAsync(Picture(), [Lamp, Bottle], Ct);

        var asked = Assert.Single(model.Asked!);
        Assert.Single(asked.Contents.OfType<DataContent>(), content => content.MediaType == "image/png");
        var words = string.Concat(asked.Contents.OfType<TextContent>().Select(text => text.Text));
        Assert.Contains("DESK_LAMP: desk lamp", words, StringComparison.Ordinal);
        Assert.Contains("bottle: bottle", words, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Asked_for_no_kind_it_asks_nothing()
    {
        var model = new Answering("[]");

        Assert.Empty(await new VisionModelObjectDetector(model).DetectAsync(Picture(), [], Ct));
        Assert.Null(model.Asked);
    }

    private sealed class Answering(string answer) : IChatClient
    {
        public IReadOnlyList<ChatMessage>? Asked { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Asked = [.. messages];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
