using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

/// <summary>
/// The model is loaded when it is first asked, and the first shopper to ask
/// would be the one who waits for it — nineteen seconds, measured. The API asks
/// first, in the background, so nobody does.
/// </summary>
public sealed class WarmVisualSearchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Both_towers_are_asked_once_with_something_they_can_read()
    {
        var model = new Model();

        await RunAsync(model);

        Assert.Equal((1, 1), (model.Pictures, model.Texts));
        // A real picture, by the same door a shopper's goes through.
        Assert.NotNull(UploadedPicture.Read(model.LastPicture.Span).Picture);
    }

    [Fact]
    public async Task A_shop_without_the_model_loads_nothing()
    {
        var model = new Model { Configured = false };

        await RunAsync(model);

        Assert.Equal((0, 0), (model.Pictures, model.Texts));
    }

    /// <summary>An exception out of a BackgroundService stops the host: a model that will not load costs visual search, not the API.</summary>
    [Fact]
    public async Task A_model_that_will_not_load_does_not_stop_the_host()
    {
        var model = new Model { Failure = new FileNotFoundException("vision.onnx") };

        await RunAsync(model);

        Assert.Equal(1, model.Pictures);
    }

    private static async Task RunAsync(Model model)
    {
        var worker = new WarmVisualSearchAtStartupWorker(model, NullLogger<WarmVisualSearchAtStartupWorker>.Instance);

        await worker.StartAsync(Ct);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    private sealed class Model : IImageEmbeddingGenerator
    {
        EmbeddingModelDescriptor IImageEmbeddingGenerator.Model => EmbeddingModelDescriptor.Siglip2Base;

        public bool Configured { get; init; } = true;

        public bool IsConfigured => Configured;

        public Exception? Failure { get; init; }
        public int Pictures { get; private set; }
        public int Texts { get; private set; }
        public ReadOnlyMemory<byte> LastPicture { get; private set; }

        public ValueTask<ReadOnlyMemory<float>> EmbedPictureAsync(ReadOnlyMemory<byte> picture, CancellationToken ct = default)
        {
            Pictures++;
            LastPicture = picture;
            return Failure is not null ? throw Failure : ValueTask.FromResult<ReadOnlyMemory<float>>(new float[4]);
        }

        public ValueTask<ReadOnlyMemory<float>> EmbedTextAsync(string text, CancellationToken ct = default)
        {
            Texts++;
            return ValueTask.FromResult<ReadOnlyMemory<float>>(new float[4]);
        }
    }
}
