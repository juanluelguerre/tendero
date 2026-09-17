using System.Diagnostics;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Answers;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// Layer B's classifier (ADR 0032), driven with a generator whose vectors the
/// test states: every similarity below is known by construction, so what is
/// asserted is the comparison, the degradation and the switch — never a model.
///
/// The anchors are built in the background, so a test that needs them awaits
/// <c>WarmAsync</c> — the build itself — rather than sleeping.
/// </summary>
public sealed class AnchorQueryScopeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly float[] InsideLike = [1, 0, 0];
    private static readonly float[] PhoneLike = [0, 1, 0];

    private const string PhoneQuery = "móvil para mi madre";

    [Fact]
    public async Task A_query_nearest_a_category_the_shop_does_not_sell_is_out_of_scope_and_named()
    {
        var scope = Scope(new Vectors(new() { [PhoneQuery] = PhoneLike }));
        await scope.WarmAsync();

        var verdict = await scope.ClassifyAsync(PhoneQuery, "es", Ct);

        Assert.Equal(new ScopeVerdict(ScopeOutcome.OutOfScope, "teléfonos móviles"), verdict);
    }

    /// <summary>
    /// Text Embeddings Inference refuses a request carrying more than 32 texts
    /// (<c>max_client_batch_size</c>, answered 422), and one culture's anchors are
    /// 133 texts against the seed taxonomy — so they are sent in batches the
    /// server accepts. Found by the protocol run of 2026-09-17, before any verdict.
    /// </summary>
    [Fact]
    public async Task Anchors_are_embedded_in_batches_the_model_server_accepts()
    {
        var generator = new Vectors(new() { [PhoneQuery] = PhoneLike }) { MaxBatchSize = 32 };
        var scope = Scope(generator);
        await scope.WarmAsync();

        var verdict = await scope.ClassifyAsync(PhoneQuery, "es", Ct);

        Assert.Equal(new ScopeVerdict(ScopeOutcome.OutOfScope, "teléfonos móviles"), verdict);
        Assert.All(generator.Calls, batch => Assert.InRange(batch.Length, 1, 32));
    }

    [Fact]
    public async Task A_query_that_names_no_product_type_is_ambiguous_and_keeps_its_results()
    {
        // As near an inside anchor as the phone anchor: neither wins by the margin.
        var scope = Scope(new Vectors(new() { ["regalo abuela"] = [0.7071f, 0.7071f, 0] }));
        await scope.WarmAsync();

        var verdict = await scope.ClassifyAsync("regalo abuela", "es", Ct);

        Assert.Equal(new ScopeVerdict(ScopeOutcome.Ambiguous), verdict);
    }

    [Fact]
    public async Task A_generator_that_fails_leaves_the_query_unclassified()
    {
        var scope = Scope(new Vectors(new() { [PhoneQuery] = PhoneLike })
        {
            FailOn = PhoneQuery,
            Failure = new HttpRequestException("the model server is down")
        });
        await scope.WarmAsync();

        var verdict = await scope.ClassifyAsync(PhoneQuery, "es", Ct);

        Assert.Equal(ScopeVerdict.Unknown, verdict);
    }

    [Fact]
    public async Task A_query_never_waits_for_anchors_a_hanging_model_server_is_still_embedding()
    {
        var generator = new Vectors(new() { [PhoneQuery] = PhoneLike }) { HangOnAnchors = true };
        var scope = Scope(generator);

        var elapsed = Stopwatch.StartNew();
        var verdicts = await Task.WhenAll(
            scope.ClassifyAsync(PhoneQuery, "es", Ct),
            scope.ClassifyAsync(PhoneQuery, "es", Ct));
        elapsed.Stop();

        Assert.Equal([ScopeVerdict.Unknown, ScopeVerdict.Unknown], verdicts);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1), $"The queries took {elapsed.Elapsed}.");

        // Both queries asked for the anchors; one build answered them both.
        await generator.AnchorBatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Single(generator.Calls);
    }

    [Fact]
    public async Task A_failed_anchor_build_is_retried_only_after_the_back_off()
    {
        var clock = new TestClock();
        var log = new Warnings();
        var generator = new Vectors(new() { [PhoneQuery] = PhoneLike }) { FailFirstCalls = 1 };
        var scope = Scope(generator, clock: clock, log: log);

        await scope.WarmAsync();
        var duringBackOff = await scope.ClassifyAsync(PhoneQuery, "es", Ct);
        await scope.WarmAsync();

        Assert.Equal(ScopeVerdict.Unknown, duringBackOff);
        Assert.Equal(1, generator.AnchorBatches);
        Assert.Equal(1, log.Count);

        clock.Advance(AnchorQueryScope.RetryAfterFailure);
        await scope.WarmAsync();
        var afterRetry = await scope.ClassifyAsync(PhoneQuery, "es", Ct);

        Assert.Equal(new ScopeVerdict(ScopeOutcome.OutOfScope, "teléfonos móviles"), afterRetry);
        Assert.Equal(3, generator.AnchorBatches); // the failed "es", then "es" and "en"
    }

    [Fact]
    public async Task A_caller_that_gives_up_is_told_so_rather_than_answered_no_verdict()
    {
        var scope = Scope(new Vectors(new() { [PhoneQuery] = PhoneLike }));
        await scope.WarmAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scope.ClassifyAsync(PhoneQuery, "es", new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task A_taxonomy_that_is_not_the_calibrated_one_switches_the_classifier_off()
    {
        var generator = new Vectors(new() { [PhoneQuery] = PhoneLike });
        var scope = Scope(generator, Calibration() with { CategoriesHash = "something else" });
        await scope.WarmAsync();

        var first = await scope.ClassifyAsync(PhoneQuery, "es", Ct);
        var second = await scope.ClassifyAsync("mobile phone for my mother", "en", Ct);

        Assert.Equal((ScopeVerdict.Unknown, ScopeVerdict.Unknown), (first, second));
        Assert.Empty(generator.Calls);
    }

    [Fact]
    public async Task A_switched_off_classifier_says_so_once()
    {
        var log = new Warnings();
        var scope = Scope(new Vectors([]), Calibration() with { CategoriesHash = "something else" }, log: log);

        await scope.ClassifyAsync(PhoneQuery, "es", Ct);
        await scope.WarmAsync();
        await scope.ClassifyAsync(PhoneQuery, "es", Ct);
        await scope.WarmAsync();

        Assert.Equal(1, log.Count);
    }

    [Fact]
    public async Task Anchor_vectors_are_computed_once_per_process()
    {
        var generator = new Vectors(new() { [PhoneQuery] = PhoneLike });
        var scope = Scope(generator);
        await scope.WarmAsync();

        var verdicts = new[]
        {
            await scope.ClassifyAsync(PhoneQuery, "es", Ct),
            await scope.ClassifyAsync(PhoneQuery, "es", Ct)
        };
        await scope.WarmAsync();

        // One batch per culture carries the anchors; every other call is a query.
        Assert.All(verdicts, verdict => Assert.Equal(ScopeOutcome.OutOfScope, verdict.Outcome));
        Assert.Equal((2, 2), (generator.AnchorBatches, generator.Calls.Count(batch => batch is [PhoneQuery])));
    }

    private static AnchorQueryScope Scope(
        Vectors generator, QueryScopeCalibration? calibration = null, TimeProvider? clock = null, Warnings? log = null) =>
        new(generator, new Categories(Tree), calibration ?? Calibration(), clock ?? TimeProvider.System, log ?? new Warnings());

    private static readonly CategoryTree Tree = BuildTree();

    private static CategoryTree BuildTree()
    {
        var home = Category.Define(TimeProvider.System, "HOME", Text("Hogar", "Home"));
        var kitchen = Category.Define(TimeProvider.System, "KITCHEN", Text("Cocina", "Kitchen"), home);
        return new CategoryTree([home, kitchen]);
    }

    /// <summary>
    /// The calibration a measurement over <see cref="Tree"/> and the committed
    /// anchor file would have written — computed here from the rule, not by
    /// asking the class under test for it.
    /// </summary>
    private static QueryScopeCalibration Calibration() =>
        new(Margin: 0.05,
            InsideAnchors: 2,
            CategoriesHash: ScopeFingerprint.OfCategories(
            [
                ("HOME", "Hogar", "Home", "Hogar", "Home"),
                ("KITCHEN", "Cocina", "Kitchen", "Hogar › Cocina", "Home › Kitchen")
            ]),
            OutsideAnchorsHash: ScopeFingerprint.OfBytes(OutOfScopeAnchors.Bytes()));

    private static LocalizedText Text(string es, string en) =>
        new(new Dictionary<string, string> { ["es"] = es, ["en"] = en });

    private sealed class Categories(CategoryTree tree) : ICategoryReader
    {
        public Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult(tree);
    }

    /// <summary>Counts warnings; everything else is ignored.</summary>
    private sealed class Warnings : ILogger<AnchorQueryScope>
    {
        private int warnings;

        public int Count => this.warnings;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Interlocked.Increment(ref this.warnings);
        }
    }

    /// <summary>
    /// A text the test names gets the vector it names. The anchor file holds
    /// about thirty outside categories, so every text NOT named maps to
    /// "inside-like" (1,0,0) — the shop's own categories and every outside
    /// anchor alike — EXCEPT the phone anchor's texts, which map to (0,1,0).
    /// So the only thing a phone-like query can be nearest to outside the shop
    /// is "teléfonos móviles" / "mobile phones".
    /// </summary>
    private sealed class Vectors(Dictionary<string, float[]> named) : IEmbeddingGenerator<string, Embedding<float>>
    {
        private static readonly HashSet<string> PhoneTexts = OutOfScopeAnchors.Load().Categories
            .Single(category => category.Code == "MOBILE_PHONES")
            .Texts.Values.SelectMany(texts => texts)
            .ToHashSet(StringComparer.Ordinal);

        private int failuresLeft = -1;

        public List<string[]> Calls { get; } = [];

        /// <summary>Batches that carried the anchors — each holds a phone anchor text.</summary>
        public int AnchorBatches
        {
            get { lock (Calls) return Calls.Count(batch => batch.Any(PhoneTexts.Contains)); }
        }

        public TaskCompletionSource AnchorBatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Only a batch containing this text fails; null fails every batch.</summary>
        public string? FailOn { get; init; }

        public Exception? Failure { get; init; }

        /// <summary>The first N calls fail, whatever they carry.</summary>
        public int FailFirstCalls { init => this.failuresLeft = value; }

        /// <summary>An anchor batch never comes back until its budget cancels it.</summary>
        public bool HangOnAnchors { get; init; }

        /// <summary>Like TEI's <c>max_client_batch_size</c>: a larger request is refused.</summary>
        public int? MaxBatchSize { get; init; }

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var texts = values.ToArray();
            lock (Calls)
                Calls.Add(texts);

            var isAnchorBatch = texts.Any(PhoneTexts.Contains);
            if (isAnchorBatch)
                AnchorBatchStarted.TrySetResult();

            if (HangOnAnchors && isAnchorBatch)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            if (texts.Length > MaxBatchSize)
                throw new HttpRequestException($"batch size {texts.Length} > maximum allowed batch size {MaxBatchSize}");

            if (Interlocked.Decrement(ref this.failuresLeft) >= 0)
                throw new HttpRequestException("the model server is starting");

            if (Failure is not null && (FailOn is null || texts.Contains(FailOn)))
                throw Failure;

            return new GeneratedEmbeddings<Embedding<float>>(texts.Select(text => new Embedding<float>(Vector(text))));
        }

        private float[] Vector(string text) =>
            named.TryGetValue(text, out var vector) ? vector
            : PhoneTexts.Contains(text) ? PhoneLike
            : InsideLike;

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
