using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Answers;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Search.Evaluation;

/// <summary>
/// The door the measurement tool uses to run layer B by its protocol (ADR 0032),
/// without widening <c>InternalsVisibleTo</c> to a tool: the classifier, its
/// calibration and the rules stay internal, and this exposes exactly what a
/// measurement needs — the declared grid and rules, the fingerprint a calibration
/// is written with, and a classifier built for one margin.
/// </summary>
public static class QueryScopeEvaluation
{
    /// <summary>The grid declared before measuring.</summary>
    public static IReadOnlyList<double> Grid => QueryScopeProtocol.Grid;

    /// <inheritdoc cref="QueryScopeProtocol.Combine"/>
    public static IReadOnlyList<MarginResult> Combine(IEnumerable<MarginResult> perCulture) =>
        QueryScopeProtocol.Combine(perCulture);

    /// <inheritdoc cref="QueryScopeProtocol.Choose"/>
    public static double? Choose(IReadOnlyList<MarginResult> dev) => QueryScopeProtocol.Choose(dev);

    /// <inheritdoc cref="QueryScopeProtocol.Accepts"/>
    public static bool Accepts(IReadOnlyList<MarginResult> scopeTestByCulture, IReadOnlyList<int> goldenInMarkedOutByCulture) =>
        QueryScopeProtocol.Accepts(scopeTestByCulture, goldenInMarkedOutByCulture);

    /// <summary>
    /// What a calibration measured today would be pinned to: the inside anchors
    /// counted and hashed the way <c>AnchorQueryScope</c> checks them at run time,
    /// and the committed outside-anchor file's hash. One source, so the file a run
    /// writes cannot disagree with the check that reads it.
    /// </summary>
    public static async Task<ScopeCalibrationRecord> FingerprintAsync(ICategoryReader categories, double margin, CancellationToken ct)
    {
        var tree = await categories.AllAsync(ct);
        var (count, categoriesHash) = AnchorQueryScope.FingerprintOf(tree);

        return new ScopeCalibrationRecord(margin, count, categoriesHash, ScopeFingerprint.OfBytes(OutOfScopeAnchors.Bytes()));
    }

    /// <summary>How many texts each culture's anchor build embeds, in the order the build embeds the cultures.</summary>
    public static async Task<IReadOnlyList<(string Culture, int Texts)>> AnchorTextCountsAsync(ICategoryReader categories, CancellationToken ct)
    {
        var tree = await categories.AllAsync(ct);
        var outside = OutOfScopeAnchors.Load();

        return
        [
            .. CultureNegotiation.Supported.Select(culture =>
            {
                var (inside, notSold) = AnchorQueryScope.AnchorTextsFor(tree, outside, culture);
                return (culture, inside.Count + notSold.Count);
            })
        ];
    }

    /// <summary>
    /// The real classifier with an in-memory calibration for <paramref name="margin"/>,
    /// its anchors already built. A classifier whose build failed answers every
    /// query <see cref="ScopeOutcome.Unknown"/>, which a measurement must treat as
    /// "could not run" rather than as a verdict.
    /// </summary>
    public static async Task<ScopeClassifier> CreateAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        ICategoryReader categories,
        double margin,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var record = await FingerprintAsync(categories, margin, ct);
        var scope = new AnchorQueryScope(
            generator,
            categories,
            new QueryScopeCalibration(record.Margin, record.InsideAnchors, record.CategoriesHash, record.OutsideAnchorsHash),
            TimeProvider.System,
            loggers.CreateLogger<AnchorQueryScope>());

        await scope.WarmAsync();
        return new ScopeClassifier(scope);
    }
}

/// <summary>A calibration as it is written to <c>query-scope.calibration.json</c>.</summary>
public sealed record ScopeCalibrationRecord(double Margin, int InsideAnchors, string CategoriesHash, string OutsideAnchorsHash);

/// <summary>One margin's classifier, as the measurement sees it.</summary>
public sealed class ScopeClassifier
{
    private readonly IQueryScope scope;

    internal ScopeClassifier(IQueryScope scope) => this.scope = scope;

    public async Task<(ScopeOutcome Outcome, string? Label)> ClassifyAsync(string query, string culture, CancellationToken ct)
    {
        var verdict = await this.scope.ClassifyAsync(query, culture, ct);
        return (verdict.Outcome, verdict.Label);
    }
}
