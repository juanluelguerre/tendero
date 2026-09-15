using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Evaluation;
using ElGuerre.Tendero.Search.Features.EvaluateSearch;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;
using static ElGuerre.Tendero.Search.Tests.Strategies;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// The backoffice panel's measurement (P8-11): the committed golden sets, both
/// strategies, the running catalogue.
/// </summary>
public sealed class EvaluateSearchHandlerTests
{
    private static readonly TestClock Clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Both_strategies_are_scored_against_the_committed_golden_sets_and_thresholds()
    {
        var lexical = new Answering(_ => []);
        var hybrid = new Answering(_ => []);
        var layer = VectorLayerStatus.Unavailable("the model server is downloading its weights");

        var result = await new EvaluateSearchHandler(new Catalogue(), lexical, hybrid, new Layer(layer))
            .HandleAsync(new EvaluateSearchQuery(), Ct);

        var goldenSets = await EmbeddedEvaluation.GoldenSetsAsync(Ct);
        var thresholds = await EmbeddedEvaluation.ThresholdsAsync(Ct);

        Assert.Equal(goldenSets.Select(set => set.Culture), result.Cultures.Select(culture => culture.Culture));
        Assert.All(result.Cultures, culture =>
        {
            var golden = goldenSets.Single(set => set.Culture == culture.Culture);

            Assert.Equal(golden.Queries.Select(query => query.Query), culture.Queries.Select(row => row.Query));
            Assert.Equal(thresholds.For("lexical", culture.Culture).NdcgAt10, culture.Lexical.ThresholdNdcgAt10);
            Assert.Equal(thresholds.For("hybrid", culture.Culture).RecallAt50, culture.Hybrid.ThresholdRecallAt50);
        });

        var queries = goldenSets.Sum(set => set.Queries.Count);
        Assert.Equal(queries, lexical.Asked.Count);
        Assert.Equal(queries, hybrid.Asked.Count);
        Assert.All(lexical.Asked, asked => Assert.Equal((1, RelevanceEvaluator.RecallCutoff), (asked.Page, asked.PageSize)));

        Assert.Equal(new VectorLayerReport(true, false, layer.Reason), result.VectorLayer);
        Assert.Equal(0, result.JudgedProductsInCatalogue);
    }

    /// <summary>
    /// The index answers with internal ids and the golden set judges source ids;
    /// the catalogue is what translates, so a product imported from the source is
    /// judged and one that was not simply is not.
    /// </summary>
    [Fact]
    public async Task A_hit_is_judged_by_the_source_id_the_golden_set_annotates()
    {
        var golden = (await EmbeddedEvaluation.GoldenSetsAsync(Ct)).Single(set => set.Culture == "es");
        var query = golden.Queries.First(candidate => candidate.Judgments.Any(judgment => judgment.Relevance == 3));
        var best = query.Judgments.First(judgment => judgment.Relevance == 3);

        var product = Product.Create(Clock, LocalizedText.From("es", "Del catálogo"), new Money(10m, "EUR"));
        product.LinkExternal(Clock, golden.Source, best.ExternalId);
        var stranger = Product.Create(Clock, LocalizedText.From("es", "De otro origen"), new Money(10m, "EUR"));

        var words = new Answering(asked => asked.Text == query.Query && asked.Culture == "es" ? [product.Id.ToString(), stranger.Id.ToString()] : []);
        var meaning = new Answering(_ => [stranger.Id.ToString()]);

        var result = await new EvaluateSearchHandler(new Catalogue(product, stranger), words, meaning, new Layer(VectorLayerStatus.NotConfigured))
            .HandleAsync(new EvaluateSearchQuery(), Ct);

        var row = result.Cultures.Single(culture => culture.Culture == "es").Queries.First(candidate => candidate.Query == query.Query);

        Assert.True(row.LexicalNdcgAt10 > 0, $"lexical NDCG@10 was {row.LexicalNdcgAt10}");
        Assert.Equal(0d, row.HybridNdcgAt10);
        Assert.Equal(1, result.JudgedProductsInCatalogue);
    }

    private sealed class Answering(Func<ProductSearchQuery, string[]> ids) : ISearchStrategy
    {
        public List<ProductSearchQuery> Asked { get; } = [];

        public Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
        {
            Asked.Add(query);
            var hits = ids(query).Select(id => Hit(id)).ToList();
            return Task.FromResult(new SearchResultPage(hits, hits.Count, query.Page, query.PageSize, 1));
        }
    }

    private sealed class Catalogue(params Product[] products) : IProductReader
    {
        public Task<Product?> FindBySkuAsync(string sku, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Product?> GetByIdAsync(ProductId id, CancellationToken ct) => throw new NotSupportedException();

        public async IAsyncEnumerable<Product> StreamAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var product in products)
            {
                await Task.Yield();
                yield return product;
            }
        }
    }

    private sealed class Layer(VectorLayerStatus status) : IVectorLayer
    {
        public Task<VectorLayerStatus> CheckAsync(CancellationToken ct = default) => Task.FromResult(status);
    }
}
