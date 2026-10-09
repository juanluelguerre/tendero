using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// Asks the picture model one picture and one word when the API starts, so the
/// first shopper who searches with a picture is not the one who loads it.
///
/// Each tower is loaded when it is first asked for — 372 MB and 1.13 GB read and
/// prepared — and on the machine this was measured on the first search by
/// picture took **18.7 s** where the ones after it took 0.27 s (2026-10-08, the
/// stack starting around it). Nobody waits nineteen seconds for a search.
///
/// In the background, never in the start-up path: the API answers everything
/// else while it loads, and a search by picture that arrives first simply waits
/// for the same load. A model that will not load is logged and costs visual
/// search, which then answers that it is not answering — never the host
/// (invariant 8). A shop without the model does nothing here.
///
/// Registered by the API alone. The worker embeds the catalogue's pictures and
/// loads the vision tower when it has one to embed; it never reads a text, and
/// would hold a gigabyte for nothing.
/// </summary>
public sealed class WarmVisualSearchAtStartupWorker(
    IImageEmbeddingGenerator model,
    ILogger<WarmVisualSearchAtStartupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!model.IsConfigured)
            return;

        // Off the thread that starts the host: loading is synchronous work.
        await Task.Yield();

        try
        {
            var stopwatch = Stopwatch.StartNew();

            await model.EmbedPictureAsync(ABlankPicture(), stoppingToken);
            await model.EmbedTextAsync("warm", stoppingToken);

            logger.LogInformation("The picture model is loaded: {Model}, {Elapsed:F1}s", model.Model.ModelId, stopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "The picture model could not be loaded at startup; searching by picture will say it is not answering");
        }
    }

    /// <summary>A white square, as small as a picture gets: what is being warmed is the model, not the decoder.</summary>
    private static byte[] ABlankPicture()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Opaque));
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
