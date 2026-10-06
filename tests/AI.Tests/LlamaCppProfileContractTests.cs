using ElGuerre.Tendero.AI.Chat;
using Microsoft.Extensions.AI;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>
/// The contract against Steward's local model, the steward-model container on its
/// pinned port. A CI runner cannot hold a 4B model, so with no container answering
/// the tests skip; they run locally with Tendero__Steward=true.
/// </summary>
public sealed class LlamaCppProfileContractTests : ChatProfileContractTests
{
    private static readonly Uri Server = new("http://localhost:38085/");

    private static readonly Lazy<bool> Running = new(() =>
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            return http.GetAsync(new Uri(Server, "health")).GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch (Exception unreachable) when (unreachable is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    });

    protected override IChatClient? Client(TimeSpan timeout) =>
        Running.Value ? OpenAICompatibleChat.CreateAt(new Uri(Server, "v1/"), "qwen3.5-4b-q8_0", timeout: timeout) : null;

    /// <summary>Whether the server was started with the vision projector (Tendero__Vision=true): it says so itself.</summary>
    private static readonly Lazy<bool> ReadsPictures = new(() =>
    {
        if (!Running.Value)
            return false;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var props = System.Text.Json.JsonDocument.Parse(http.GetStringAsync(new Uri(Server, "props")).GetAwaiter().GetResult());
        return props.RootElement.TryGetProperty("modalities", out var modalities)
            && modalities.TryGetProperty("vision", out var sees) && sees.ValueKind == System.Text.Json.JsonValueKind.True;
    });

    /// <summary>
    /// A picture as the shop stores it — WebP — sent through the shop's own factory
    /// is seen by the model, and its tokens are counted (phase 13a, I-2). The picture
    /// is one colour, so what is asked has one answer.
    /// </summary>
    [Fact]
    public async Task A_stored_picture_sent_through_the_shops_own_factory_is_seen_and_metered()
    {
        Assert.SkipUnless(ReadsPictures.Value, "needs the local model started with its vision projector (Tendero__Vision=true)");

        using var bitmap = new SkiaSharp.SKBitmap(256, 256);
        bitmap.Erase(SkiaSharp.SKColors.Red);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var webp = image.Encode(SkiaSharp.SKEncodedImageFormat.Webp, 90);

        var answer = await Client(TimeSpan.FromSeconds(120))!.GetResponseAsync(
            [new ChatMessage(ChatRole.User, [Pictures.ForModel(webp.ToArray()), new TextContent("What single colour fills this picture? Answer with one word.")])],
            new ChatOptions { Temperature = 0, MaxOutputTokens = 20 },
            TestContext.Current.CancellationToken);

        Assert.Contains("red", answer.Text, StringComparison.OrdinalIgnoreCase);
        // A picture is prompt tokens, far more than the dozen the question is.
        Assert.True(answer.Usage!.InputTokenCount > 50, $"only {answer.Usage.InputTokenCount} tokens in");
    }
}
