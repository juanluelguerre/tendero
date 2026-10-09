using System.Text;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.Search.Qdrant;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Qdrant.Client.Grpc;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The picture projection against a real Qdrant, at the image the AppHost runs
/// (P13-3).
///
/// **The model is a fake, as it is for the words** (<see cref="QdrantVectorLayerTests"/>):
/// SigLIP 2 is 1.5 GB outside git and held to its maker's embeddings by a suite of
/// its own. What is under test here is everything around it — one point a
/// picture, who it names, when it is embedded and when it is not — and that needs
/// only vectors that are deterministic and near when the pictures are. A
/// "picture" here is a few words in a file, hashed into buckets.
/// </summary>
[Trait("Category", "Integration")]
public sealed class QdrantPictureVectorsTests(QdrantVectorLayerTests.QdrantContainer qdrant)
    : IClassFixture<QdrantVectorLayerTests.QdrantContainer>
{
    private const int Dimensions = 64;
    private static readonly TestClock Clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_picture_is_one_point_naming_its_product_and_the_model_that_wrote_it()
    {
        var layer = Layer();
        var bike = AProduct(layer.Store.Hold("red bike"), layer.Store.Hold("red bike wheel"));

        Assert.True(await layer.Pictures.IsEmptyAsync(Ct));
        await layer.Pictures.IndexAsync(bike, Ct);

        Assert.Equal(2UL, await qdrant.Client.CountAsync(layer.Pictures.Collection, exact: true, cancellationToken: Ct));
        var point = Assert.Single(await qdrant.Client.RetrieveAsync(
            layer.Pictures.Collection, [(PointId)PicturePointId.For(bike.Images[0].Id)], withPayload: true, withVectors: false, cancellationToken: Ct));

        Assert.Equal(bike.Images[0].Id.Value, point.Payload[PictureFields.ImageId].StringValue);
        Assert.Equal([bike.Id.ToString()], point.Payload[PictureFields.Products].ListValue.Values.Select(value => value.StringValue));
        Assert.Equal(layer.Model.ModelId, point.Payload[PointFields.ModelId].StringValue);
        Assert.Equal(layer.Model.Revision, point.Payload[PointFields.ModelRevision].StringValue);
    }

    /// <summary>
    /// A product raises ProductUpserted for a price and for a stock count. Its
    /// pictures did not change, so the model is not asked and the store is not read.
    /// </summary>
    [Fact]
    public async Task A_product_upserted_again_embeds_nothing()
    {
        var layer = Layer();
        var bike = AProduct(layer.Store.Hold("red bike"), layer.Store.Hold("red bike wheel"));

        await layer.Pictures.IndexAsync(bike, Ct);
        await layer.Pictures.IndexAsync(bike, Ct);
        await layer.Pictures.IndexAsync(bike, Ct);

        Assert.Equal(2, layer.Model_.Pictures);
        Assert.Equal(2, layer.Store.Opened);
    }

    /// <summary>The dedup that is free: the picture's id is the hash of its bytes, and the point is keyed on it.</summary>
    [Fact]
    public async Task The_same_picture_under_two_products_is_embedded_once_and_names_both()
    {
        var layer = Layer();
        var shared = layer.Store.Hold("steel bottle");
        var (one, another) = (AProduct(shared), AProduct(shared));

        await layer.Pictures.IndexAsync(one, Ct);
        await layer.Pictures.IndexAsync(another, Ct);

        Assert.Equal(1, layer.Model_.Pictures);
        Assert.Equal(1UL, await qdrant.Client.CountAsync(layer.Pictures.Collection, exact: true, cancellationToken: Ct));
        Assert.Equal(
            new[] { one.Id.ToString(), another.Id.ToString() }.Order(),
            (await layer.Pictures.NearestAsync(layer.Model_.Vector("steel bottle"), 5, Ct)).Select(match => match.ProductId).Order());

        // One of them goes: the picture is still the other's.
        await layer.Pictures.RemoveAsync(one.Id, Ct);
        Assert.Equal(another.Id.ToString(), Assert.Single(await layer.Pictures.NearestAsync(layer.Model_.Vector("steel bottle"), 5, Ct)).ProductId);

        // And the other: nobody shows it, and the point goes.
        await layer.Pictures.RemoveAsync(another.Id, Ct);
        Assert.True(await layer.Pictures.IsEmptyAsync(Ct));
    }

    [Fact]
    public async Task A_picture_taken_out_of_the_gallery_leaves_the_collection()
    {
        var layer = Layer();
        var (front, side) = (layer.Store.Hold("blue kettle front"), layer.Store.Hold("blue kettle side"));
        var kettle = AProduct(front, side);
        await layer.Pictures.IndexAsync(kettle, Ct);

        kettle.ReplaceImages(Clock, [(side, null)]);
        await layer.Pictures.IndexAsync(kettle, Ct);

        var match = Assert.Single(await layer.Pictures.NearestAsync(layer.Model_.Vector("blue kettle front"), 5, Ct));
        Assert.Equal(side.Value, match.ImageId);
        Assert.Equal(1UL, await qdrant.Client.CountAsync(layer.Pictures.Collection, exact: true, cancellationToken: Ct));
    }

    /// <summary>
    /// A product with three pictures would otherwise take three of the nearest
    /// places and push the second product off a short list.
    /// </summary>
    [Fact]
    public async Task The_nearest_are_products_each_once_by_the_nearest_of_its_pictures()
    {
        var layer = Layer();
        var bikeFront = layer.Store.Hold("red bike front");
        var bike = AProduct(bikeFront, layer.Store.Hold("red bike side"), layer.Store.Hold("red bike wheel"));
        var scooter = AProduct(layer.Store.Hold("red scooter"));
        var kettle = AProduct(layer.Store.Hold("steel kettle"));
        foreach (var product in new[] { bike, scooter, kettle })
            await layer.Pictures.IndexAsync(product, Ct);

        var nearest = await layer.Pictures.NearestAsync(layer.Model_.Vector("red bike front"), 2, Ct);

        Assert.Equal([bike.Id.ToString(), scooter.Id.ToString()], nearest.Select(match => match.ProductId));
        Assert.Equal(bikeFront.Value, nearest[0].ImageId);
        Assert.True(nearest[0].Similarity > nearest[1].Similarity);
    }

    /// <summary>A gallery may name a picture whose bytes never arrived; the ones that did are still embedded.</summary>
    [Fact]
    public async Task A_picture_the_store_does_not_hold_is_skipped_and_the_rest_are_embedded()
    {
        var layer = Layer();
        var lamp = AProduct(new ImageId("sha256-never-stored"), layer.Store.Hold("brass lamp"));

        await layer.Pictures.IndexAsync(lamp, Ct);

        Assert.Equal(1UL, await qdrant.Client.CountAsync(layer.Pictures.Collection, exact: true, cancellationToken: Ct));
    }

    [Fact]
    public async Task Nothing_written_yet_is_an_empty_answer_and_not_an_error()
    {
        var layer = Layer();

        Assert.Empty(await layer.Pictures.NearestAsync(layer.Model_.Vector("anything"), 5, Ct));
        await layer.Pictures.RemoveAsync(ProductId.New(), Ct);
    }

    [Fact]
    public async Task Recreating_empties_the_collection()
    {
        var layer = Layer();
        await layer.Pictures.IndexAsync(AProduct(layer.Store.Hold("red bike")), Ct);

        await layer.Pictures.RecreateAsync(Ct);

        Assert.True(await layer.Pictures.IsEmptyAsync(Ct));
    }

    /// <summary>The collection's name already says the model; the point says it again, and reading one that disagrees is refused.</summary>
    [Fact]
    public async Task A_point_written_by_another_model_is_refused_when_it_is_read()
    {
        var layer = Layer();
        await layer.Pictures.IndexAsync(AProduct(layer.Store.Hold("red bike")), Ct);

        await qdrant.Client.UpsertAsync(layer.Pictures.Collection,
        [
            new PointStruct
            {
                Id = Guid.NewGuid(),
                Vectors = layer.Model_.Vector("red bike").ToArray(),
                Payload =
                {
                    [PictureFields.ImageId] = "sha256-foreign",
                    [PictureFields.Products] = new[] { ProductId.New().ToString() },
                    [PointFields.ModelId] = "somebody/else",
                    [PointFields.ModelRevision] = layer.Model.Revision,
                },
            },
        ], cancellationToken: Ct);

        var refusal = await Assert.ThrowsAsync<SearchUnavailableException>(() =>
            layer.Pictures.NearestAsync(layer.Model_.Vector("red bike"), 5, Ct));

        Assert.Contains("somebody/else", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_vector_of_another_size_is_refused_before_it_reaches_the_store()
    {
        var layer = Layer();
        await layer.Pictures.IndexAsync(AProduct(layer.Store.Hold("red bike")), Ct);

        var refusal = await Assert.ThrowsAsync<SearchUnavailableException>(() => layer.Pictures.NearestAsync(new float[Dimensions / 2], 5, Ct));

        // Ours, with both sizes in it — not whatever the store would have said.
        Assert.Contains($"a vector of {Dimensions / 2} dimensions", refusal.Message, StringComparison.Ordinal);
    }

    private TestLayer Layer()
    {
        // Without Docker the suite skips with the reason rather than failing.
        if (qdrant.Unavailable is { } reason)
            Assert.Skip(reason);

        // A model of its own per test, and so a collection of its own: the
        // container is shared by the class and the tests may run in any order.
        var model = new EmbeddingModelDescriptor(
            $"tests/pictures-{Guid.NewGuid():N}", Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + "00000000", Dimensions);
        var generator = new WordsAsPictures(model);
        var store = new HeldPictures();

        return new TestLayer(model, generator, store, new QdrantPictureVectors(qdrant.Client, generator, store));
    }

    private static Product AProduct(params ImageId[] gallery)
    {
        var product = Product.Create(Clock, LocalizedText.From("es", "Producto").With("en", "Product"), new Money(29.90m, "EUR"));
        product.AddVariant(Clock, $"SKU-{Guid.NewGuid():N}", new Money(29.90m, "EUR"));
        foreach (var picture in gallery)
            product.AddImage(Clock, picture);
        product.Publish(Clock);
        return product;
    }

    private sealed record TestLayer(
        EmbeddingModelDescriptor Model, WordsAsPictures Model_, HeldPictures Store, QdrantPictureVectors Pictures);

    /// <summary>A content-addressed store in memory, counting how often it was read.</summary>
    private sealed class HeldPictures : IImageStore
    {
        private readonly Dictionary<ImageId, byte[]> held = [];

        public int Opened { get; private set; }

        public ImageId Hold(string words)
        {
            var id = new ImageId("sha256-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(words))));
            this.held[id] = Encoding.UTF8.GetBytes(words);
            return id;
        }

        public Task<ImageId> SaveAsync(Stream content, string contentType, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<StoredImage?> OpenAsync(ImageId id, CancellationToken ct = default)
        {
            Opened++;
            return Task.FromResult(this.held.TryGetValue(id, out var bytes) ? new StoredImage(new MemoryStream(bytes), "image/png") : null);
        }
    }

    /// <summary>Words into buckets, whether they came as a picture's bytes or as text: shared words mean nearness.</summary>
    private sealed class WordsAsPictures(EmbeddingModelDescriptor model) : IImageEmbeddingGenerator
    {
        public EmbeddingModelDescriptor Model => model;

        public int Pictures { get; private set; }

        public ValueTask<ReadOnlyMemory<float>> EmbedPictureAsync(ReadOnlyMemory<byte> picture, CancellationToken ct = default)
        {
            Pictures++;
            return ValueTask.FromResult(Vector(Encoding.UTF8.GetString(picture.Span)));
        }

        public ValueTask<ReadOnlyMemory<float>> EmbedTextAsync(string text, CancellationToken ct = default) => ValueTask.FromResult(Vector(text));

        public ReadOnlyMemory<float> Vector(string text)
        {
            var vector = new float[model.Dimensions];

            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                vector[(int)((uint)word.Aggregate(17, (hash, character) => unchecked(hash * 31 + character)) % model.Dimensions)] += 1;

            vector[0] += 0.01f; // never the zero vector, whose cosine is undefined
            var length = MathF.Sqrt(vector.Sum(value => value * value));
            return vector.Select(value => value / length).ToArray();
        }
    }
}
