using System.Net;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Embeddings;
using ElGuerre.Tendero.Search.Qdrant;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The vector projection against a real Qdrant, at the image the AppHost runs.
///
/// **The model is a fake, and deliberately so.** The real one is 2.2 GB and opt-in
/// (<see cref="EmbeddingModelServerTests"/>); what is under test here is everything
/// around it — the points, their ids, the payload, the refusal of a mixed vector
/// space — and none of that needs meaning, only vectors that are deterministic and
/// close when the words are. Hashing words into buckets gives exactly that, and it
/// runs on every CI build.
/// </summary>
[Trait("Category", "Integration")]
public sealed class QdrantVectorLayerTests(QdrantVectorLayerTests.QdrantContainer qdrant)
    : IClassFixture<QdrantVectorLayerTests.QdrantContainer>
{
    private const int Dimensions = 64;
    private static readonly TestClock Clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_active_product_is_one_point_per_culture_carrying_the_model_that_wrote_it()
    {
        var layer = Layer();
        var product = ACoffeeMaker();

        await layer.Vectors.IndexAsync(product, Ct);

        var points = await qdrant.Client.ScrollAsync(
            layer.Model.CollectionName, limit: 10, payloadSelector: true, cancellationToken: Ct);
        Assert.Equal(["en", "es"], points.Result.Select(point => point.Payload["culture"].StringValue).Order());
        Assert.All(points.Result, point =>
        {
            Assert.Equal(product.Id.ToString(), point.Payload["productId"].StringValue);
            Assert.Equal(layer.Model.ModelId, point.Payload["modelId"].StringValue);
            Assert.Equal(layer.Model.Revision, point.Payload["modelRevision"].StringValue);
            Assert.Equal(["HOME", "KITCHEN"], point.Payload["categoryCodes"].ListValue.Values.Select(value => value.StringValue));
        });
    }

    [Fact]
    public async Task A_product_whose_words_did_not_change_is_not_embedded_again()
    {
        var layer = Layer();
        var product = ACoffeeMaker();

        await layer.Vectors.IndexAsync(product, Ct);
        await layer.Vectors.IndexAsync(product, Ct);
        Assert.Equal(2, layer.Generator.Texts);

        // A new price changes nothing a shopper reads in the text...
        product.AddVariant(Clock, "COFFEE-XL", new Money(39.90m, "EUR"));
        await layer.Vectors.IndexAsync(product, Ct);
        Assert.Equal(2, layer.Generator.Texts);

        // ...and a new name does, in both cultures.
        product.UpdateDetails(Clock, Text("Cafetera italiana grande", "Large Italian coffee maker"), null, "Kuisina", "KITCHEN");
        await layer.Vectors.IndexAsync(product, Ct);
        Assert.Equal(4, layer.Generator.Texts);
    }

    [Fact]
    public async Task Removing_a_product_takes_both_points_and_the_collection_is_empty_again()
    {
        var layer = Layer();
        var product = ACoffeeMaker();

        Assert.True(await layer.Vectors.IsEmptyAsync(Ct));
        await layer.Vectors.IndexAsync(product, Ct);
        Assert.False(await layer.Vectors.IsEmptyAsync(Ct));

        await ProductIndexProjection.ApplyAsync(layer.Vectors, Archived(product), Ct);

        Assert.True(await layer.Vectors.IsEmptyAsync(Ct));
    }

    [Fact]
    public async Task Search_finds_by_words_within_one_culture_and_one_branch()
    {
        var layer = Layer();
        var coffee = ACoffeeMaker();
        var backpack = AProduct(Text("Mochila urbana", "City backpack"), "BAGS");

        await layer.Vectors.IndexAsync(coffee, Ct);
        await layer.Vectors.IndexAsync(backpack, Ct);

        var english = await layer.Vectors.SearchAsync("coffee maker", "en", category: null, limit: 10, Ct);
        Assert.Equal(coffee.Id.ToString(), english[0].ProductId);
        Assert.Equal(2, english.Count); // one point per product in the culture, never its Spanish twin

        var inTheKitchen = await layer.Vectors.SearchAsync("mochila", "es", category: "HOME", limit: 10, Ct);
        Assert.Equal([coffee.Id.ToString()], inTheKitchen.Select(match => match.ProductId));

        var texts = await layer.Vectors.TextsAsync([coffee.Id.ToString()], "es", Ct);
        Assert.StartsWith("Cafetera italiana. Kuisina. Hogar Cocina", texts[coffee.Id.ToString()]);
    }

    [Fact]
    public async Task A_collection_of_another_size_makes_the_layer_unavailable_and_recreating_repairs_it()
    {
        var layer = Layer();
        await qdrant.Client.CreateCollectionAsync(
            layer.Model.CollectionName, new VectorParams { Size = 8, Distance = Distance.Cosine }, cancellationToken: Ct);

        var status = await layer.Layer.CheckAsync(Ct);
        Assert.True(status.IsConfigured);
        Assert.False(status.IsAvailable);
        Assert.Contains("holds 8", status.Reason);
        await Assert.ThrowsAsync<SearchUnavailableException>(() => layer.Vectors.IndexAsync(ACoffeeMaker(), Ct));

        await layer.Vectors.RecreateAsync(Ct);

        Assert.True((await layer.Layer.CheckAsync(Ct)).IsAvailable);
        await layer.Vectors.IndexAsync(ACoffeeMaker(), Ct);
    }

    [Fact]
    public async Task A_model_server_serving_another_revision_makes_the_layer_unavailable()
    {
        var layer = Layer(servedRevision: new string('b', 40));

        var status = await layer.Layer.CheckAsync(Ct);

        Assert.False(status.IsAvailable);
        Assert.Contains(new string('b', 40), status.Reason);
        Assert.False(await qdrant.Client.CollectionExistsAsync(layer.Model.CollectionName, Ct));
    }

    [Fact]
    public async Task A_point_written_by_another_model_is_refused_when_it_is_read()
    {
        var layer = Layer();
        var product = ACoffeeMaker();
        await layer.Vectors.IndexAsync(product, Ct);

        await qdrant.Client.SetPayloadAsync(
            layer.Model.CollectionName,
            new Dictionary<string, Value> { ["modelRevision"] = new string('c', 40) },
            filter: Conditions.MatchKeyword("culture", "en"),
            cancellationToken: Ct);

        var refusal = await Assert.ThrowsAsync<SearchUnavailableException>(() =>
            layer.Vectors.SearchAsync("coffee maker", "en", null, 10, Ct));

        Assert.Contains("mix two vector spaces", refusal.Message);
    }

    private TestLayer Layer(string? servedRevision = null)
    {
        // Without Docker the suite skips with the reason rather than failing, like
        // every other integration test: a fresh clone can still run `dotnet test`.
        if (qdrant.Unavailable is { } reason)
            Assert.Skip(reason);

        // A model of its own per test, and so a collection of its own: the
        // container is shared by the class and the tests may run in any order.
        var model = new EmbeddingModelDescriptor(
            $"tests/words-{Guid.NewGuid():N}", Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + "00000000", Dimensions);

        var info = new ModelServerInfo(new HttpClient(new ModelServer(model.ModelId, servedRevision ?? model.Revision))
        {
            BaseAddress = new Uri("http://model-server/")
        });

        var vectorLayer = new QdrantVectorLayer(qdrant.Client, info, model, TimeProvider.System, NullLogger<QdrantVectorLayer>.Instance);
        var generator = new WordBuckets(Dimensions);

        return new TestLayer(model, vectorLayer, generator,
            new QdrantProductVectors(qdrant.Client, generator, model, vectorLayer, new NoDefinitions(), new Branches()));
    }

    private static Product ACoffeeMaker() => AProduct(Text("Cafetera italiana", "Italian coffee maker"), "KITCHEN");

    private static Product AProduct(LocalizedText name, string category)
    {
        var product = Product.Create(Clock, name, new Money(29.90m, "EUR"), brand: "Kuisina", category: category);
        product.AddVariant(Clock, $"{category}-DEFAULT", new Money(29.90m, "EUR"));
        product.Publish(Clock);
        return product;
    }

    private static Product Archived(Product product)
    {
        product.Archive(Clock);
        return product;
    }

    private static LocalizedText Text(string es, string en) => LocalizedText.From("es", es).With("en", en);

    private sealed record TestLayer(
        EmbeddingModelDescriptor Model, QdrantVectorLayer Layer, WordBuckets Generator, QdrantProductVectors Vectors);

    public sealed class QdrantContainer : IAsyncLifetime
    {
        private IContainer? container;

        public QdrantClient Client { get; private set; } = null!;

        public async ValueTask InitializeAsync()
        {
            this.container = new ContainerBuilder("qdrant/qdrant:v1.18.0")
                .WithLabel(DockerDesktopGroup.ProjectLabel, DockerDesktopGroup.Project)
                .WithLabel(DockerDesktopGroup.ServiceLabel, "test-qdrant")
                .WithPortBinding(6334, assignRandomHostPort: true)
                .WithPortBinding(6333, assignRandomHostPort: true)
                .WithEnvironment("QDRANT__SERVICE__API_KEY", "integration")
                .WithEnvironment("QDRANT__TELEMETRY_DISABLED", "true")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(6333).ForPath("/readyz")))
                .Build();

            try
            {
                await this.container.StartAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Unavailable = $"Could not start Qdrant: {exception.Message}";
                return;
            }

            Client = new QdrantClient(
                new Uri($"http://{this.container.Hostname}:{this.container.GetMappedPublicPort(6334)}"), apiKey: "integration");
        }

        public string? Unavailable { get; private set; }

        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            if (this.container is not null)
                await this.container.DisposeAsync();
        }
    }

    /// <summary>Words into buckets: equal words land in equal dimensions, so shared words mean nearness.</summary>
    private sealed class WordBuckets(int dimensions) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public int Texts { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var texts = values.ToList();
            Texts += texts.Count;
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(texts.Select(text => new Embedding<float>(Vector(text)))));
        }

        private float[] Vector(string text)
        {
            var vector = new float[dimensions];

            foreach (var word in text.ToLowerInvariant().Split([' ', '.', ','], StringSplitOptions.RemoveEmptyEntries))
                vector[(int)((uint)StableHash(word) % dimensions)] += 1;

            vector[0] += 0.01f; // never the zero vector, whose cosine is undefined
            var length = MathF.Sqrt(vector.Sum(value => value * value));
            return [.. vector.Select(value => value / length)];
        }

        private static int StableHash(string word) =>
            word.Aggregate(17, (hash, character) => unchecked(hash * 31 + character));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ModelServer(string modelId, string revision) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"model_id":"{{modelId}}","model_sha":"{{revision}}"}""", Encoding.UTF8, "application/json")
            });
    }

    private sealed class NoDefinitions : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AttributeDefinitions.Empty);
    }

    private sealed class Branches : ICategoryReader
    {
        public Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default)
        {
            var home = Category.Define(Clock, "HOME", Text("Hogar", "Home"));
            var kitchen = Category.Define(Clock, "KITCHEN", Text("Cocina", "Kitchen"), home);
            var bags = Category.Define(Clock, "BAGS", Text("Bolsos", "Bags"));
            return Task.FromResult(new CategoryTree([home, kitchen, bags]));
        }
    }
}
