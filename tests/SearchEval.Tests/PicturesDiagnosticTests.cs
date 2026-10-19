using ElGuerre.Tendero.Search;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.SearchEval.Diagnostics;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// What the tool adds to measure the pictures (phase 13a, P13-4): an option
/// that only means something where the vector store is, a diagnostic strategy
/// that ranks by the pictures alone, and the seed's pictures held in memory.
/// </summary>
public sealed class PicturesDiagnosticTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_pictures_are_measured_where_the_vector_store_is()
    {
        Assert.Equal("models/siglip2", EvaluationOptions.Parse(["--strategy", "hybrid", "--siglip", "models/siglip2"]).Siglip);
        Assert.Null(EvaluationOptions.Parse(["--strategy", "hybrid"]).Siglip);

        var alone = Assert.Throws<ArgumentException>(() => EvaluationOptions.Parse(["--siglip", "models/siglip2"]));
        Assert.Contains("--strategy hybrid", alone.Message, StringComparison.Ordinal);
    }

    /// <summary>The objects suite asks SigLIP about parts of a photograph, with no vector store at all.</summary>
    [Fact]
    public void The_objects_suite_takes_the_export_without_the_vector_store()
    {
        var options = EvaluationOptions.Parse(["--suite", "objects", "--siglip", "models/siglip2"]);

        Assert.Equal(("models/siglip2", Path.Combine("artifacts", "picture-eval")), (options.Siglip, options.Pictures));
    }

    /// <summary>
    /// The ruler grades a ranking, so the ranking is the pictures': nearest
    /// first, with the similarity as the score, and a product the culture's index
    /// does not hold left out rather than counted as a miss of the pictures.
    /// </summary>
    [Fact]
    public async Task Words_are_ranked_by_the_pictures_alone_with_the_cards_the_index_holds()
    {
        var model = new Words();
        var strategy = new PicturesByTextStrategy(
            model, new Nearest(new("lamp", "a", 0.14), new("gone", "b", 0.11), new("stool", "c", 0.09)), new Cards("stool", "lamp"));

        var page = await strategy.SearchAsync(new ProductSearchQuery("lámpara de latón", "es", Page: 1, PageSize: 10), Ct);

        Assert.Equal(["lamp", "stool"], page.Hits.Select(hit => hit.ProductId));
        Assert.Equal([0.14, 0.09], page.Hits.Select(hit => hit.Score));
        Assert.Equal("lámpara de latón", model.Asked);
    }

    [Fact]
    public async Task The_second_page_is_the_next_products_and_the_pictures_are_asked_for_that_many()
    {
        var pictures = new Nearest(new("a", "1", 0.5), new("b", "2", 0.4), new("c", "3", 0.3));
        var strategy = new PicturesByTextStrategy(new Words(), pictures, new Cards("a", "b", "c"));

        var page = await strategy.SearchAsync(new ProductSearchQuery("lamp", "en", Page: 2, PageSize: 2), Ct);

        Assert.Equal("c", Assert.Single(page.Hits).ProductId);
        Assert.Equal((3L, 4), (page.Total, pictures.AskedFor));
    }

    /// <summary>Keyed on the bytes, as the shop's store is: the same file twice is one picture.</summary>
    [Fact]
    public async Task The_seeds_pictures_are_held_by_what_they_are()
    {
        var held = new HeldPictures();

        var first = await held.SaveAsync(new MemoryStream([1, 2, 3]), "image/webp", Ct);
        var again = await held.SaveAsync(new MemoryStream([1, 2, 3]), "image/webp", Ct);
        var other = await held.SaveAsync(new MemoryStream([4, 5, 6]), "image/webp", Ct);

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.Equal(2, held.All.Count);

        var opened = await held.OpenAsync(first, Ct);
        Assert.Equal("image/webp", opened!.ContentType);
        Assert.Equal(3, opened.Content.Length);
        Assert.Null(await held.OpenAsync(new ImageId("sha256-never"), Ct));
    }

    private sealed class Words : IImageEmbeddingGenerator
    {
        public EmbeddingModelDescriptor Model => EmbeddingModelDescriptor.Siglip2Base;

        public string? Asked { get; private set; }

        public ValueTask<ReadOnlyMemory<float>> EmbedPictureAsync(ReadOnlyMemory<byte> picture, CancellationToken ct = default) =>
            throw new NotSupportedException("Words are asked here, never a picture.");

        public ValueTask<ReadOnlyMemory<float>> EmbedTextAsync(string text, CancellationToken ct = default)
        {
            Asked = text;
            return ValueTask.FromResult<ReadOnlyMemory<float>>(new float[4]);
        }
    }

    private sealed class Nearest(params PictureMatch[] near) : IPictureSearch
    {
        public int AskedFor { get; private set; }

        public Task<IReadOnlyList<PictureMatch>> NearestAsync(ReadOnlyMemory<float> vector, int limit, IReadOnlyCollection<string>? among = null, CancellationToken ct = default)
        {
            AskedFor = limit;
            return Task.FromResult<IReadOnlyList<PictureMatch>>(near);
        }
    }

    /// <summary>The culture's index: it holds these products and answers them in no promised order — here, reversed.</summary>
    private sealed class Cards(params string[] held) : ISearchHitReader
    {
        public Task<IReadOnlyList<SearchHit>> FindAsync(
            IReadOnlyCollection<string> productIds, string culture, string? category, SearchFilters filters, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SearchHit>>(
            [
                .. held.Where(productIds.Contains).Reverse().Select(id => new SearchHit(
                    id, $"CODE-{id}", $"Product {id}", $"product-{id}", null, null, $"{id}-variant", $"{id}-SKU",
                    new Money(9.90m, "EUR"), new Money(9.90m, "EUR"), new Money(9.90m, "EUR"), null, InStock: true, Score: 0)),
            ]);
    }
}
