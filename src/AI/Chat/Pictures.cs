using Microsoft.Extensions.AI;
using SkiaSharp;

namespace ElGuerre.Tendero.AI.Chat;

/// <summary>
/// A picture on its way to a model (phase 13a, I-2). The shop's store keeps WebP
/// and llama.cpp's server decodes PNG and JPEG — measured in the spike
/// (<c>docs/analysis/image-models.md</c>) — so what is stored is never what is
/// sent: it is decoded and written again as PNG, in memory. Whoever sends a
/// picture to a model does it through here, so there is one place that knows.
/// </summary>
public static class Pictures
{
    private const string Png = "image/png";

    /// <summary>The stored picture as a model takes it.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a picture.</exception>
    public static DataContent ForModel(ReadOnlySpan<byte> stored)
    {
        // Asked of the codec first: handed bytes that are no image, SKBitmap.Decode
        // throws about a parameter of its own instead of returning nothing.
        using var data = SKData.CreateCopy(stored);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("These bytes are not a picture a model can be sent.");
        using var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("These bytes are not a picture a model can be sent.");
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return new DataContent(encoded.ToArray(), Png);
    }

    /// <summary>How many pictures a message carries.</summary>
    public static int In(ChatMessage message) =>
        message.Contents.OfType<DataContent>().Count(content => content.HasTopLevelMediaType("image"));

    /// <summary>
    /// For a client that reads only a message's text: a picture it would drop is a
    /// question answered about something it never saw, so it says it cannot.
    /// </summary>
    public static void RefuseIn(IEnumerable<ChatMessage> messages, string who)
    {
        if (messages.Any(message => In(message) > 0))
            throw new NotSupportedException($"{who} does not read pictures: send them to a profile that says it does (ReadsPictures).");
    }
}
