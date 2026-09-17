using System.Diagnostics;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.SearchEval.Scope;

/// <summary>
/// Layer B (ADR 0032): whether a query names something the shop does not sell,
/// decided by which is nearer — the shop's own categories, or a committed list of
/// categories it does not carry — with <see cref="ScopeDecision"/> doing the
/// comparison and a calibrated margin deciding how clear the win must be.
///
/// <para>
/// <b>It is measured, not shipped</b> (ADR 0032, amended 2026-09-17). It was the
/// one candidate the protocol has run, and the protocol refuted it; it lives
/// here so the next candidate is measured against the same anchors, the same
/// rules and the same queries rather than against a memory of them.
/// </para>
///
/// <para>
/// <b>Every failure is "no verdict"</b>, which the protocol treats as "did not
/// run" rather than as an answer. A model server that is down, slow or answering
/// nonsense leaves the run with nothing to count; nothing here ever throws past
/// the caller, except the caller's own cancellation.
/// </para>
///
/// <para>
/// <b>It switches itself off</b> when the taxonomy or the committed anchor file
/// is not the one its margin was measured against (D5), and says so once. A
/// margin chosen for 23 categories has no authority over 400.
/// </para>
///
/// <para>
/// The query is embedded here rather than reused from the vector side: the
/// vector port answers product ids, not the query's vector (a recorded deviation
/// from the design, and the reason the run reports its own latency).
/// </para>
/// </summary>
public sealed class AnchorQueryScope(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    ICategoryReader categories,
    QueryScopeCalibration calibration,
    TimeProvider clock,
    ILogger<AnchorQueryScope> logger) : IQueryScope
{
    /// <summary>The joiner a shopper reads in a breadcrumb, "Hogar › Cocina › Menaje de cocina".</summary>
    public const string PathSeparator = " › ";

    /// <summary>The same budget the vector side has (P8-7), so a measurement says what a shop would have paid.</summary>
    public static readonly TimeSpan QueryBudget = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// How long one culture's anchors may take to embed — a few dozen texts,
    /// which a CPU needs longer for than one query. The build runs in the
    /// background, so this bounds a shared build, never a shopper's request.
    /// </summary>
    public static readonly TimeSpan AnchorBudget = TimeSpan.FromSeconds(10);

    /// <summary>
    /// After a failed build, how long before another is tried: the same rhythm
    /// <c>QdrantVectorLayer</c> remembers a "no" for, so a model server that is
    /// down is asked every ten seconds rather than on every query.
    /// </summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Text Embeddings Inference's default <c>max_client_batch_size</c>: a request
    /// carrying more texts is refused with a 422. One culture's anchors are 133
    /// texts against the seed taxonomy, so they go in batches of this size — found
    /// by the protocol run of 2026-09-17, which could not build them in one.
    /// </summary>
    public const int MaxBatchSize = 32;

    // Its own source, named after the tool it now lives in: this classifier ran
    // inside the shop until 2026-09-17, and borrowing the shop's telemetry source
    // from a command-line tool would file its spans under an assembly that no
    // longer contains it.
    private static readonly ActivitySource Telemetry = new("ElGuerre.Tendero.SearchEval");

    // Null until a build finishes; then either Off (for good) or the anchors (for good).
    private volatile Prepared? prepared;

    // The one build in flight, if any. Written only through Interlocked, so two
    // queries arriving together start one build between them.
    private Task? building;

    // UTC ticks before which no new build starts, after a failed one.
    private long retryAfterTicks;

    /// <summary>
    /// <b>A query never waits for the anchors.</b> Until they exist, it starts
    /// the background build (once, and not inside the back-off after a failure)
    /// and is answered "no verdict" at once — so a model server that hangs costs
    /// a caller nothing, instead of queueing every request behind the build. The
    /// measurement calls <see cref="WarmAsync"/> first for exactly that reason.
    /// </summary>
    public async Task<ScopeVerdict> ClassifyAsync(string text, string culture, CancellationToken ct = default)
    {
        using var activity = Telemetry.StartActivity("search.scope");

        try
        {
            var state = this.prepared;
            if (state is null)
            {
                _ = WarmAsync();
                activity?.SetTag("search.scope.unavailable", "anchors not ready");
                activity?.SetTag("search.scope.outcome", ScopeOutcome.Unknown.ToString());
                return ScopeVerdict.Unknown;
            }

            if (state.IsOff || !state.Anchors.TryGetValue(Culture.Normalize(culture), out var anchors))
            {
                activity?.SetTag("search.scope.outcome", ScopeOutcome.Unknown.ToString());
                return ScopeVerdict.Unknown;
            }

            var query = await EmbedAsync([text], QueryBudget, ct);
            var verdict = Decide(query[0], anchors, out var inside, out var outside);

            activity?.SetTag("search.scope.outcome", verdict.Outcome.ToString());
            activity?.SetTag("search.scope.inside", inside);
            activity?.SetTag("search.scope.outside", outside);
            return verdict;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            activity?.SetTag("search.scope.unavailable", exception.GetType().Name);
            activity?.SetTag("search.scope.outcome", ScopeOutcome.Unknown.ToString());
            logger.LogDebug(exception, "Query scope gave no verdict for this query.");
            return ScopeVerdict.Unknown;
        }
    }

    /// <summary>
    /// Starts the anchor build if nothing is built, nothing is building and the
    /// back-off has passed, and returns the build in flight — or a completed task
    /// when there is nothing to do. Never faults: a failed build is logged and
    /// remembered. Public so the measurement — and a test — can wait for the build
    /// instead of sleeping.
    /// </summary>
    public Task WarmAsync()
    {
        if (this.prepared is not null)
            return Task.CompletedTask;

        if (Volatile.Read(ref this.building) is { } inFlight)
            return inFlight;

        if (clock.GetUtcNow().UtcTicks < Interlocked.Read(ref this.retryAfterTicks))
            return Task.CompletedTask;

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref this.building, done.Task, null) is { } raced)
            return raced;

        _ = Task.Run(async () =>
        {
            try
            {
                await BuildAsync();
            }
            finally
            {
                Volatile.Write(ref this.building, null);
                done.SetResult();
            }
        });

        return done.Task;
    }

    /// <summary>
    /// The classifier one margin's run uses, with an in-memory calibration for
    /// that margin and its anchors already built.
    ///
    /// <para>
    /// The calibration is computed here rather than read from a file: a run
    /// measures the taxonomy and the anchor file it is looking at, so the margin
    /// under test is never refused by its own fingerprint check. A classifier
    /// whose build failed answers every query <see cref="ScopeOutcome.Unknown"/>,
    /// which the caller must treat as "could not run" rather than as a verdict.
    /// </para>
    /// </summary>
    public static async Task<AnchorQueryScope> MeasuringAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        ICategoryReader categories,
        double margin,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var scope = new AnchorQueryScope(
            generator,
            categories,
            await CalibrationForAsync(categories, margin, ct),
            TimeProvider.System,
            loggers.CreateLogger<AnchorQueryScope>());

        await scope.WarmAsync();
        return scope;
    }

    /// <summary>
    /// What a calibration measured today would be pinned to: the inside anchors
    /// counted and hashed the way <see cref="BuildAsync"/> checks them, and the
    /// committed outside-anchor file's hash. One source, so a file a run writes
    /// cannot disagree with the check that reads it.
    /// </summary>
    public static async Task<QueryScopeCalibration> CalibrationForAsync(
        ICategoryReader categories, double margin, CancellationToken ct)
    {
        var tree = await categories.AllAsync(ct);
        var (count, categoriesHash) = FingerprintOf(tree);

        return new QueryScopeCalibration(margin, count, categoriesHash, ScopeFingerprint.OfBytes(OutOfScopeAnchors.Bytes()));
    }

    /// <summary>How many texts each culture's anchor build embeds, in the order the build embeds the cultures.</summary>
    public static async Task<IReadOnlyList<(string Culture, int Texts)>> AnchorTextCountsAsync(
        ICategoryReader categories, CancellationToken ct)
    {
        var tree = await categories.AllAsync(ct);
        var outside = OutOfScopeAnchors.Load();

        return
        [
            .. CultureNegotiation.Supported.Select(culture =>
            {
                var (inside, notSold) = AnchorTextsFor(tree, outside, culture);
                return (culture, inside.Count + notSold.Count);
            })
        ];
    }

    /// <summary>
    /// The inside anchors' identity, as the calibration stores it: one row per
    /// category, with its name and its breadcrumb path in both cultures. Shared
    /// with <see cref="CalibrationForAsync"/>, so a calibration a run writes and
    /// the check that reads it cannot disagree about what was measured.
    /// </summary>
    public static (int Count, string Hash) FingerprintOf(CategoryTree tree) =>
        (tree.All.Count,
         ScopeFingerprint.OfCategories(tree.All.Select(category => (
             category.Code,
             category.Name.In("es"),
             category.Name.In("en"),
             PathIn(tree, category, "es"),
             PathIn(tree, category, "en")))));

    /// <summary>
    /// A category's branch as a shopper reads it. <see cref="CategoryTree.PathTextIn"/>
    /// joins the same names with spaces, which is right for matching words and
    /// wrong for an anchor: "Hogar Cocina" embeds as a phrase, "Hogar › Cocina" as a place.
    /// </summary>
    public static string PathIn(CategoryTree tree, Category category, string culture) =>
        string.Join(PathSeparator, category.Ancestry
            .Select(code => tree.ByCode(code)?.Name.In(culture))
            .Where(name => !string.IsNullOrWhiteSpace(name)));

    /// <summary>
    /// One build: the fingerprint first, then the anchors. It runs with no
    /// caller's token — a request that gives up must not abort a build every
    /// later request shares — and each culture is bounded by <see cref="AnchorBudget"/>.
    /// </summary>
    private async Task BuildAsync()
    {
        if (this.prepared is not null)
            return;

        try
        {
            var tree = await categories.AllAsync(CancellationToken.None);
            var (count, categoriesHash) = FingerprintOf(tree);
            var outsideHash = ScopeFingerprint.OfBytes(OutOfScopeAnchors.Bytes());

            if (count != calibration.InsideAnchors
                || categoriesHash != calibration.CategoriesHash
                || outsideHash != calibration.OutsideAnchorsHash)
            {
                logger.LogWarning(
                    "Query scope is off: the taxonomy or the anchor file changed since the margin was measured ({Expected} → {Actual}). Re-run SearchEval --suite scope.",
                    $"{calibration.InsideAnchors} categories, {calibration.CategoriesHash}, {calibration.OutsideAnchorsHash}",
                    $"{count} categories, {categoriesHash}, {outsideHash}");

                this.prepared = Prepared.Off;
                return;
            }

            var outside = OutOfScopeAnchors.Load();
            var anchors = new Dictionary<string, CultureAnchors>(StringComparer.Ordinal);

            foreach (var culture in CultureNegotiation.Supported)
                anchors[culture] = await EmbedAnchorsAsync(tree, outside, culture, CancellationToken.None);

            this.prepared = new Prepared(IsOff: false, anchors);
        }
        catch (Exception exception)
        {
            // One warning per failed build, and a build is tried at most once per
            // back-off window — so a server that stays down is one line every ten
            // seconds, never one per query. No query text: the build has none.
            Interlocked.Exchange(ref this.retryAfterTicks, clock.GetUtcNow().Add(RetryAfterFailure).UtcTicks);
            logger.LogWarning(exception,
                "Query scope anchors could not be embedded; no query gets a verdict until a retry in {RetryAfter}.",
                RetryAfterFailure);
        }
    }

    /// <summary>
    /// One culture's anchor texts: the shop's categories (name and breadcrumb path,
    /// once each) and the committed texts of what it does not sell. Shared with
    /// <see cref="AnchorTextCountsAsync"/>, which reports the build's cost per culture.
    /// </summary>
    public static (IReadOnlyList<string> Inside, IReadOnlyList<Anchor> Outside) AnchorTextsFor(
        CategoryTree tree, OutOfScopeAnchorFile outside, string culture)
    {
        // A root category's name and its path are the same text; embedding it twice buys nothing.
        var inside = tree.All
            .SelectMany(category => new[] { category.Name.In(culture), PathIn(tree, category, culture) })
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var notSold = outside.Categories
            .Where(category => category.Texts.ContainsKey(culture) && category.Label.ContainsKey(culture))
            .SelectMany(category => category.Texts[culture]
                .Select(text => new Anchor(category.Code, culture, text, category.Label[culture])))
            .ToList();

        return (inside, notSold);
    }

    /// <summary>
    /// One culture's anchors, the inside texts first and then the outside ones,
    /// split back by position — in as few requests as <see cref="MaxBatchSize"/> allows.
    /// </summary>
    private async Task<CultureAnchors> EmbedAnchorsAsync(
        CategoryTree tree, OutOfScopeAnchorFile outside, string culture, CancellationToken ct)
    {
        var (inside, notSold) = AnchorTextsFor(tree, outside, culture);

        var vectors = await EmbedAsync([.. inside, .. notSold.Select(anchor => anchor.Text)], AnchorBudget, ct);

        return new CultureAnchors(
            vectors[..inside.Count],
            [.. notSold.Select((anchor, i) => (vectors[inside.Count + i], anchor.Label))]);
    }

    private async Task<float[][]> EmbedAsync(string[] texts, TimeSpan budget, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(budget, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        linked.Token.ThrowIfCancellationRequested();

        // One budget for the whole set, however many requests it takes.
        var vectors = new List<float[]>(texts.Length);
        foreach (var batch in texts.Chunk(MaxBatchSize))
        {
            var embeddings = await generator.GenerateAsync(batch, cancellationToken: linked.Token);
            if (embeddings.Count != batch.Length)
                throw new InvalidOperationException($"Asked for {batch.Length} embeddings and received {embeddings.Count}.");

            vectors.AddRange(embeddings.Select(embedding => Normalised(embedding.Vector.Span)));
        }

        return [.. vectors];
    }

    private ScopeVerdict Decide(float[] query, CultureAnchors anchors, out double inside, out double outside)
    {
        inside = anchors.Inside.Count == 0 ? double.NegativeInfinity : anchors.Inside.Max(anchor => Dot(query, anchor));

        outside = double.NegativeInfinity;
        string? label = null;
        foreach (var (vector, anchorLabel) in anchors.Outside)
        {
            var similarity = Dot(query, vector);
            if (similarity > outside)
                (outside, label) = (similarity, anchorLabel);
        }

        if (double.IsNegativeInfinity(inside) || double.IsNegativeInfinity(outside))
            return ScopeVerdict.Unknown;

        var outcome = ScopeDecision.Decide(inside, outside, calibration.Margin);
        return new ScopeVerdict(outcome, outcome == ScopeOutcome.OutOfScope ? label : null);
    }

    /// <summary>
    /// Not assumed to arrive unit-length: a cosine is a dot product only between
    /// normalised vectors, and a model server's defaults are not ours to trust.
    /// </summary>
    private static float[] Normalised(ReadOnlySpan<float> vector)
    {
        var length = 0.0;
        foreach (var value in vector)
            length += value * value;

        length = Math.Sqrt(length);
        var result = vector.ToArray();
        if (length == 0)
            return result;

        for (var i = 0; i < result.Length; i++)
            result[i] = (float)(result[i] / length);

        return result;
    }

    private static double Dot(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new InvalidOperationException($"A {a.Length}-dimension query cannot be compared with a {b.Length}-dimension anchor.");

        var sum = 0.0;
        for (var i = 0; i < a.Length; i++)
            sum += a[i] * (double)b[i];

        return sum;
    }

    private sealed record CultureAnchors(IReadOnlyList<float[]> Inside, IReadOnlyList<(float[] Vector, string Label)> Outside);

    private sealed record Prepared(bool IsOff, IReadOnlyDictionary<string, CultureAnchors> Anchors)
    {
        public static readonly Prepared Off = new(IsOff: true, new Dictionary<string, CultureAnchors>());
    }
}
