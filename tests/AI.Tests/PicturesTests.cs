using ElGuerre.Tendero.AI.Chat;
using Microsoft.Extensions.AI;
using SkiaSharp;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>
/// A picture on its way to a model (phase 13a, I-2). The store keeps WebP and the
/// model server decodes PNG and JPEG, so what is stored is never what is sent; and
/// a client that reads only a message's text must refuse a picture, not drop it.
/// </summary>
public sealed class PicturesTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];

    internal static byte[] Encoded(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        return encoded.ToArray();
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Webp)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    public void A_stored_picture_goes_to_a_model_as_png_whatever_it_was_stored_as(SKEncodedImageFormat stored)
    {
        var content = Pictures.ForModel(Encoded(stored));

        Assert.Equal("image/png", content.MediaType);
        Assert.Equal(PngSignature, content.Data.ToArray()[..4]);
    }

    [Fact]
    public void What_is_not_a_picture_is_refused_by_name()
    {
        var failure = Assert.Throws<InvalidDataException>(() => Pictures.ForModel([1, 2, 3]));

        Assert.Contains("not a picture", failure.Message);
    }

    [Fact]
    public void The_pictures_of_a_message_are_counted_and_its_words_are_not()
    {
        var picture = Pictures.ForModel(Encoded(SKEncodedImageFormat.Webp));
        var words = new ChatMessage(ChatRole.User, "What is this?");
        var both = new ChatMessage(ChatRole.User, [picture, picture, new TextContent("What are these?")]);
        // A file that is no image is not a picture, whatever else it is.
        var document = new ChatMessage(ChatRole.User, [new DataContent(new byte[] { 1 }, "application/pdf")]);

        Assert.Equal((0, 2, 0), (Pictures.In(words), Pictures.In(both), Pictures.In(document)));
    }

    /// <summary>A client that reads only text would answer about a picture it never saw: it says it cannot, instead.</summary>
    [Fact]
    public void A_client_that_reads_only_text_refuses_a_conversation_with_a_picture_in_it()
    {
        var picture = Pictures.ForModel(Encoded(SKEncodedImageFormat.Webp));
        List<ChatMessage> withOne = [new(ChatRole.System, "Describe it."), new(ChatRole.User, [picture, new TextContent("Go.")])];
        List<ChatMessage> withNone = [new(ChatRole.System, "Describe it."), new(ChatRole.User, "Go.")];

        var failure = Assert.Throws<NotSupportedException>(() => Pictures.RefuseIn(withOne, "The in-process ONNX profile"));

        Assert.Contains("The in-process ONNX profile does not read pictures", failure.Message);
        Pictures.RefuseIn(withNone, "The in-process ONNX profile");
    }

    /// <summary>What a profile can do is declared, so a feature that needs a picture read asks before it calls.</summary>
    [Fact]
    public void A_profile_reads_pictures_only_when_it_says_so()
    {
        Assert.False(new ModelProfile().ReadsPictures);
        Assert.True(new ModelProfile { ReadsPictures = true }.ReadsPictures);
    }
}
