using SkiaSharp;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// A picture somebody outside the shop sent to search with (phase 13a, I-6),
/// looked at before anything is done with it: how heavy, what it really is, and
/// how large.
///
/// **What it is, is decided by the bytes** — by the decoder that is about to
/// read them, not by the header the caller wrote and not by the file's name.
/// JPEG, PNG and WebP: what a phone, a screenshot and a browser produce. Nothing
/// that moves, and nothing that is a document.
///
/// **Only its header is read here.** The size comes from the first bytes; the
/// pixels are decoded once, by the model's own preprocessing, and exist only for
/// as long as that call. Nothing of the picture is kept, and nothing but these
/// three facts ever reaches a log or a span.
/// </summary>
/// <param name="Format">The format the bytes are, lower case: <c>jpeg</c>, <c>png</c> or <c>webp</c>.</param>
public sealed record UploadedPicture(int Width, int Height, string Format)
{
    /// <summary>
    /// Eight megabytes: a phone's photograph as it comes, with room. The bound is
    /// the server's because the endpoint can be called by anything.
    /// </summary>
    public const long MaxBytes = 8 * 1024 * 1024;

    /// <summary>
    /// The largest side taken. Decoded, a picture is four bytes a pixel: at this
    /// bound that is 64 MB for the moment the model reads it, and at the 8192 the
    /// catalogue's own pictures are allowed it would be 256 MB for anyone who asked.
    /// </summary>
    public const int MaxSide = 4096;

    /// <summary>Why a picture heavier than <see cref="MaxBytes"/> is not taken, said before its bytes are read.</summary>
    public static LocalizedText TooHeavy { get; } = LocalizedText.Both(
        $"A picture to search with is at most {MaxBytes / (1024 * 1024)} MB.",
        $"Una foto para buscar pesa como mucho {MaxBytes / (1024 * 1024)} MB.");

    /// <summary>
    /// Whether the picture is stored on its side: its orientation turns it a
    /// quarter, so it is shown — and its objects are placed — with width and
    /// height swapped.
    /// </summary>
    public bool Turned { get; init; }

    /// <summary>The width as the picture is seen, after its orientation: the coordinates a box in it is given in.</summary>
    public int UprightWidth => this.Turned ? this.Height : this.Width;

    /// <summary>The height as the picture is seen, after its orientation.</summary>
    public int UprightHeight => this.Turned ? this.Width : this.Height;

    /// <returns>The picture's facts, or why it is not taken — a sentence for the caller, in the API's own language.</returns>
    public static (UploadedPicture? Picture, string? Refusal) Read(ReadOnlySpan<byte> bytes)
    {
        var (picture, refusal) = Check(bytes);
        return (picture, refusal?.In("en"));
    }

    /// <returns>The picture's facts, or why it is not taken in both of the shop's languages (invariant 6), for an endpoint that answers with a coded problem (ADR 0059).</returns>
    public static (UploadedPicture? Picture, LocalizedText? Refusal) Check(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return (null, LocalizedText.Both("The picture is empty.", "La foto está vacía."));
        if (bytes.Length > MaxBytes)
            return (null, TooHeavy);

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);

        var format = codec?.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => "jpeg",
            SKEncodedImageFormat.Png => "png",
            SKEncodedImageFormat.Webp => "webp",
            _ => null,
        };

        if (codec is null || format is null)
            return (null, LocalizedText.Both(
                "A picture to search with is a JPEG, a PNG or a WebP.",
                "Una foto para buscar es un JPEG, un PNG o un WebP."));
        if (codec.Info.Width > MaxSide || codec.Info.Height > MaxSide)
            return (null, LocalizedText.Both(
                $"The picture is {codec.Info.Width} by {codec.Info.Height}; the largest taken is {MaxSide} a side.",
                $"La foto mide {codec.Info.Width} por {codec.Info.Height}; se acepta como mucho {MaxSide} de lado."));

        return (new UploadedPicture(codec.Info.Width, codec.Info.Height, format)
        {
            Turned = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom,
        }, null);
    }
}
