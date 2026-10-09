using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.Search.Strategies;
using ElGuerre.Tendero.SharedKernel;
using Xunit;
using static ElGuerre.Tendero.Search.Tests.Strategies;

namespace ElGuerre.Tendero.Search.Tests.Images;

/// <summary>
/// The pictures as a third leg of the hybrid search (phase 13a): off unless a
/// depth says otherwise, a third list in the fusion when it is on, and — the rule
/// it is held to — **something that can only add to a search, never cost it**.
/// </summary>
public sealed class PictureLegTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly HybridSearchOptions On = new() { PictureDepth = 10 };

    /// <summary>
    /// The default. A shop with the model and the store, and no depth set, searches
    /// exactly as it did: the model is not asked and the answer is the same bytes.
    /// </summary>
    [Fact]
    public async Task Off_by_default_the_pictures_are_never_asked_and_the_answer_is_what_it_was()
    {
        var model = new Words();
        var looks = new Looks("e", "b");
        var query = new ProductSearchQuery("regalo para casa", "es");

        var without = await Hybrid(new Lexical("a", "b", "c"), new Vectors(("b", 0.8), ("d", 0.7)), new Cards("d", "e")).SearchAsync(query, Ct);
        var with = await Hybrid(new Lexical("a", "b", "c"), new Vectors(("b", 0.8), ("d", 0.7)), new Cards("d", "e"), pictureModel: model, pictures: looks)
            .SearchAsync(query, Ct);

        Assert.Equal(Json(without), Json(with));
        Assert.Equal((0, 0), (model.Asked, looks.Asked));
    }

    [Fact]
    public async Task On_what_only_a_picture_found_joins_the_page_and_what_three_lists_agree_on_ranks_first()
    {
        var lexical = new Lexical("a", "b", "c") { AnswersTheUnion = true };
        var looks = new Looks("b", "e");

        var result = await Hybrid(lexical, new Vectors(("b", 0.8), ("d", 0.7)), new Cards("d", "e"), options: On, pictureModel: new Words(), pictures: looks)
            .SearchAsync(new ProductSearchQuery("regalo para casa", "es"), Ct);

        Assert.Equal("b", result.Hits[0].ProductId);
        Assert.Equal((1d / 62) + (1d / 61) + (1d / 61), result.Hits[0].Score, precision: 12);
        Assert.Contains("e", result.Hits.Select(hit => hit.ProductId));
        Assert.Equal(5, result.Total);
        Assert.Equal(10, looks.Limit);

        // The page's total and facets are counted by the lexical engine, so it is told about every product on it.
        Assert.Equal(["b", "d", "e"], lexical.Asked.Single().AlsoMatching!.Order());
    }

    /// <summary>The whole rule: a picture model that throws, or a store that is down, leaves the two-legged search standing — and says so.</summary>
    [Fact]
    public async Task A_picture_leg_that_fails_costs_only_itself_and_is_counted_as_its_own_stage()
    {
        var (spans, counted) = Listen(out var text);
        var query = new ProductSearchQuery(text, "en");

        var twoLegs = await Hybrid(new Lexical("a", "b"), new Vectors(("b", 0.9), ("d", 0.5)), new Cards("d")).SearchAsync(query, Ct);
        var failing = await Hybrid(new Lexical("a", "b"), new Vectors(("b", 0.9), ("d", 0.5)), new Cards("d"), options: On,
                pictureModel: new Words(), pictures: new Looks("e") { Failure = new SearchUnavailableException("picture search", "Qdrant is down") })
            .SearchAsync(query, Ct);

        Assert.Equal(twoLegs.Hits.Select(hit => hit.ProductId), failing.Hits.Select(hit => hit.ProductId));
        Assert.Contains(spans, span => (string?)span.GetTagItem(SearchDegradation.Tag) == SearchDegradation.PictureStage);
        Assert.Equal([SearchDegradation.PictureStage], counted);
    }

    [Fact]
    public async Task A_picture_leg_that_never_answers_is_left_behind_within_its_budget()
    {
        var options = On with { PictureBudget = TimeSpan.FromMilliseconds(50) };

        var result = await Hybrid(new Lexical("a", "b"), new Vectors(("b", 0.9)), options: options, pictureModel: new Words(), pictures: new Looks("e") { Hangs = true })
            .SearchAsync(new ProductSearchQuery("lamp", "en"), Ct);

        Assert.Equal(["b", "a"], result.Hits.Select(hit => hit.ProductId));
    }

    /// <summary>Never having had the pictures is not the pictures being down: nothing is asked and nothing is counted.</summary>
    [Fact]
    public async Task A_shop_without_the_picture_model_searches_on_two_legs_and_counts_nothing()
    {
        var (_, counted) = Listen(out var text);
        var looks = new Looks("e");

        var result = await Hybrid(new Lexical("a", "b"), new Vectors(("b", 0.9)), options: On, pictureModel: new NoImageEmbeddings(), pictures: looks)
            .SearchAsync(new ProductSearchQuery(text, "en"), Ct);

        Assert.Equal(["b", "a"], result.Hits.Select(hit => hit.ProductId));
        Assert.Equal(0, looks.Asked);
        Assert.Empty(counted);
    }

    /// <summary>The picture store knows nothing of branches: inside a category its nearest would be products of any.</summary>
    [Fact]
    public async Task Inside_a_category_the_pictures_are_not_asked()
    {
        var looks = new Looks("e");

        await Hybrid(new Lexical("a", "b"), new Vectors(("b", 0.9)), options: On, pictureModel: new Words(), pictures: looks)
            .SearchAsync(new ProductSearchQuery("lamp", "en", Category: "LIGHTING"), Ct);

        Assert.Equal(0, looks.Asked);
    }

    /// <summary>A product the filters exclude must not take a rank another would have had — the nearest list's own rule.</summary>
    [Fact]
    public async Task With_filters_a_picture_match_that_fails_them_is_cut_before_fusion()
    {
        var cards = new Cards("d", "e") { PassingFilters = ["d"] };

        var result = await Hybrid(new Lexical("a"), new Vectors(("d", 0.9)), cards, options: On, pictureModel: new Words(), pictures: new Looks("e", "d"))
            .SearchAsync(new ProductSearchQuery("lamp", "en", Filters: new SearchFilters { Brands = ["Kuisina"] }), Ct);

        Assert.DoesNotContain("e", result.Hits.Select(hit => hit.ProductId));
        // d is first in the meaning list and FIRST in the pictures' once e is cut, not second.
        Assert.Equal((1d / 61) + (1d / 61), result.Hits.Single(hit => hit.ProductId == "d").Score, precision: 12);
    }

    /// <summary>
    /// Without filters the pictures' list fuses whole, as the nearest list does: a
    /// picture of something nobody can buy keeps the rank it took, and what comes
    /// after it is not promoted into its place.
    /// </summary>
    [Fact]
    public async Task Without_filters_the_pictures_list_fuses_whole()
    {
        var result = await Hybrid(new Lexical("a", "b"), new Vectors(("a", 0.9)), new Cards(), options: On, pictureModel: new Words(), pictures: new Looks("x", "b"))
            .SearchAsync(new ProductSearchQuery("lamp", "en"), Ct);

        Assert.DoesNotContain("x", result.Hits.Select(hit => hit.ProductId));
        // b is second among the words and SECOND among the pictures, behind the one with no card.
        Assert.Equal((1d / 62) + (1d / 62), result.Hits.Single(hit => hit.ProductId == "b").Score, precision: 12);
    }

    private static string Json(SearchResultPage page) => JsonSerializer.Serialize(page with { TookMs = 0 });

    private sealed class Words : IImageEmbeddingGenerator
    {
        public EmbeddingModelDescriptor Model => EmbeddingModelDescriptor.Siglip2Base;

        public int Asked { get; private set; }

        public ValueTask<ReadOnlyMemory<float>> EmbedPictureAsync(ReadOnlyMemory<byte> picture, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public ValueTask<ReadOnlyMemory<float>> EmbedTextAsync(string text, CancellationToken ct = default)
        {
            Asked++;
            return ValueTask.FromResult<ReadOnlyMemory<float>>(new float[4]);
        }
    }

    private sealed class Looks(params string[] nearest) : IPictureSearch
    {
        public Exception? Failure { get; init; }
        public bool Hangs { get; init; }
        public int Asked { get; private set; }
        public int Limit { get; private set; }

        public async Task<IReadOnlyList<PictureMatch>> NearestAsync(ReadOnlyMemory<float> vector, int limit, CancellationToken ct = default)
        {
            Asked++;
            Limit = limit;

            if (Hangs)
                await Task.Delay(Timeout.Infinite, ct);

            if (Failure is not null)
                throw Failure;

            return [.. nearest.Select((id, at) => new PictureMatch(id, "img-" + id, 0.2 - (at * 0.01)))];
        }
    }

    /// <summary>The span and the degradation counter of one search, told apart by a text only it uses.</summary>
    private static (List<Activity> Spans, List<string> Counted) Listen(out string text)
    {
        var probe = $"picture leg probe {Guid.NewGuid():N}";
        text = probe;

        var spans = new List<Activity>();
        var counted = new List<string>();

        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => source.Name == TelemetrySources.Search,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "search.hybrid" && (string?)activity.GetTagItem("search.text") == probe)
                    lock (spans) spans.Add(activity);
            },
        });

        var meters = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == TelemetrySources.Search && instrument.Name == SearchDegradation.CounterName)
                    listener.EnableMeasurementEvents(instrument);
            },
        };
        meters.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            // Only what is counted while this search's own span is the current one.
            if ((string?)Activity.Current?.GetTagItem("search.text") != probe)
                return;

            foreach (var tag in tags)
                if (tag.Key == "stage")
                    lock (counted) counted.Add((string)tag.Value!);
        });
        meters.Start();

        return (spans, counted);
    }
}
