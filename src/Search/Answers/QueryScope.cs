using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ElGuerre.Tendero.Search.Answers;

/// <summary>
/// Layer B's verdict on one comparison between a query's embedding and the two
/// nearest anchors — the shop's own categories and a committed list of what it
/// does not sell (ADR 0032). This is a contrast, not a threshold on either
/// similarity alone: the research behind ADR 0032 found every vendor semantic
/// cutoff (Algolia NeuralSearch, Weaviate autocut) relative to a query's own
/// top result, never a fixed score, because a fixed score cannot tell "the shop
/// has nothing" apart from "the shop has something unusual".
/// </summary>
/// <remarks>
/// Public rather than <c>internal</c> like the rest of this file: a
/// <c>[Theory]</c> test method must be public for xUnit to discover it
/// (analyzer rule xUnit1000), and a public method cannot take a less-visible
/// type as a parameter (CS0051) — <c>QueryScopeDecisionTests</c> takes this
/// enum directly from <c>InlineData</c>. Everything else layer B exposes today
/// (<see cref="ScopeVerdict"/>, <see cref="IQueryScope"/>, the anchor types)
/// stays internal, since nothing outside <c>Search</c> reads them yet.
/// </remarks>
public enum ScopeOutcome
{
    /// <summary>No embedding generator was configured, or it failed or ran past its
    /// budget — nothing was compared, and this is never a degradation: layer B is
    /// an experiment kept only if it earns its place, not a required stage.</summary>
    Unknown,

    /// <summary>The nearest thing the shop sells is closer than the nearest thing it
    /// does not, by at least the margin.</summary>
    InScope,

    /// <summary>Neither side wins by the margin. This is the band that protects
    /// gift, use-case and symptom queries — "regalo abuela", "algo para no
    /// mojarme en la bici" — which name no product type and sit at a comparable
    /// distance from an inside anchor and an outside one alike.</summary>
    Ambiguous,

    /// <summary>The nearest thing the shop does NOT sell is closer, by at least the
    /// margin. Only this outcome changes what the shopper sees.</summary>
    OutOfScope
}

/// <param name="Outcome">What the comparison decided.</param>
/// <param name="Label">
/// The outside anchor's label, when <see cref="Outcome"/> is
/// <see cref="ScopeOutcome.OutOfScope"/> — what the shop tells the shopper it
/// does not sell ("teléfonos móviles" / "mobile phones"). The model picked
/// which anchor was nearest from a closed, committed set; it never phrases the
/// sentence itself (invariant 12).
/// </param>
internal sealed record ScopeVerdict(ScopeOutcome Outcome, string? Label = null)
{
    public static readonly ScopeVerdict Unknown = new(ScopeOutcome.Unknown);
}

/// <summary>
/// The port Task 10 wires to a real embedding comparison. <see cref="NoQueryScope"/>
/// is the stand-in until then, and remains the stand-in on any machine with no
/// model server — never registered as failing, per <see cref="ScopeOutcome.Unknown"/>.
/// </summary>
internal interface IQueryScope
{
    Task<ScopeVerdict> ClassifyAsync(string text, string culture, CancellationToken ct = default);
}

/// <summary>
/// One point layer B compares a query's embedding against: an inside category
/// (its localized name or its localized path, "Hogar › Cocina › Menaje de
/// cocina") or an outside one (one of the committed descriptive texts below).
/// </summary>
internal sealed record Anchor(string Code, string Culture, string Text, string Label);

/// <summary>The whole committed file, deserialized as-is.</summary>
internal sealed record OutOfScopeAnchorFile(IReadOnlyList<OutOfScopeCategory> Categories);

/// <param name="Code">A stable identifier, never shown to a shopper (<c>MOBILE_PHONES</c>).</param>
/// <param name="Label">What a shopper reads after "No vendemos …" / "We don't sell …", per culture.</param>
/// <param name="Texts">Two or three short descriptive phrases per culture, embedded and compared.</param>
internal sealed record OutOfScopeCategory(
    string Code,
    IReadOnlyDictionary<string, string> Label,
    IReadOnlyDictionary<string, string[]> Texts);

/// <param name="Margin">The margin chosen on <c>scope-dev</c> (the design's protocol).</param>
/// <param name="InsideAnchors">How many inside anchors existed when the margin was chosen.</param>
/// <param name="CategoriesHash">
/// <see cref="ScopeFingerprint.OfCategories"/> over the taxonomy at that time.
/// </param>
/// <param name="OutsideAnchorsHash">
/// <see cref="ScopeFingerprint.OfBytes"/> over the committed anchor file at that time.
/// </param>
/// <remarks>
/// A margin chosen for 23 categories cannot silently judge a taxonomy of four
/// hundred (D5): when the running taxonomy or file no longer matches this
/// calibration, layer B turns itself off and the shop keeps layer A.
/// </remarks>
internal sealed record QueryScopeCalibration(double Margin, int InsideAnchors, string CategoriesHash, string OutsideAnchorsHash)
{
    private const string ResourceName = "ElGuerre.Tendero.Search.Answers.query-scope.calibration.json";

    /// <summary>
    /// The calibration compiled into this assembly, or <c>null</c> when none was
    /// committed — which is what keeps layer B off until a margin has been
    /// measured by the declared protocol. Absent is a decision, not an error.
    /// </summary>
    public static QueryScopeCalibration? LoadEmbedded()
    {
        using var stream = typeof(QueryScopeCalibration).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            return null;

        return JsonSerializer.Deserialize<QueryScopeCalibration>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException($"'{ResourceName}' is empty or not a query-scope calibration.");
    }
}

/// <summary>
/// The pure comparison at the centre of layer B — no embeddings, no I/O, so it
/// is exhaustively testable on its own (ADR 0032, "Decision rule").
/// </summary>
internal static class ScopeDecision
{
    /// <summary>
    /// Larger than any rounding error a cosine-similarity subtraction can
    /// produce, and far smaller than any margin the calibration protocol would
    /// choose (0.02 is the smallest value on the declared grid) — so it corrects
    /// floating-point noise around a real tie without ever masking a real one.
    /// </summary>
    private const double Tolerance = 1e-12;

    /// <summary>
    /// <c>outside − inside ≥ margin</c> → out of scope; <c>inside − outside ≥
    /// margin</c> → in scope; otherwise ambiguous. A query sitting EXACTLY at the
    /// margin counts as deciding, not as a tie: "0.50 minus 0.45" is
    /// 0.049999999999999989 as a <see cref="double"/>, not exactly 0.05, so a
    /// naive comparison would misfile it as ambiguous — <see cref="Snap"/> treats
    /// a difference within <see cref="Tolerance"/> of the margin as the margin
    /// itself before comparing.
    /// </summary>
    public static ScopeOutcome Decide(double insideSimilarity, double outsideSimilarity, double margin)
    {
        if (Snap(outsideSimilarity - insideSimilarity, margin) >= margin)
            return ScopeOutcome.OutOfScope;

        return Snap(insideSimilarity - outsideSimilarity, margin) >= margin
            ? ScopeOutcome.InScope
            : ScopeOutcome.Ambiguous;
    }

    private static double Snap(double difference, double margin) =>
        Math.Abs(difference - margin) <= Tolerance ? margin : difference;
}

/// <summary>
/// A fingerprint over the data a calibrated margin depends on, so the margin
/// can tell when it no longer applies (D5) rather than silently misjudging a
/// taxonomy or an anchor file it was never measured against.
/// </summary>
internal static class ScopeFingerprint
{
    /// <summary>
    /// Field and row separators are control characters that cannot appear in a
    /// category name or path, so two categories can never be crafted to collide
    /// across a boundary the way a comma or a pipe could. Ordered by
    /// <paramref name="categories"/>' code so the fingerprint does not depend on
    /// the order the taxonomy happens to be read in.
    /// </summary>
    public static string OfCategories(IEnumerable<(string Code, string Es, string En, string PathEs, string PathEn)> categories)
    {
        const char fieldSeparator = '';
        const char rowSeparator = '';

        var rows = categories
            .OrderBy(category => category.Code, StringComparer.Ordinal)
            .Select(category => string.Join(fieldSeparator, category.Code, category.Es, category.En, category.PathEs, category.PathEn));

        return OfBytes(Encoding.UTF8.GetBytes(string.Join(rowSeparator, rows)));
    }

    public static string OfBytes(ReadOnlySpan<byte> file) =>
        Convert.ToHexStringLower(SHA256.HashData(file));
}

/// <summary>
/// The categories the shop does NOT sell (ADR 0032, layer B), embedded from
/// <c>Answers/out-of-scope-anchors.json</c> — written by us, before any
/// measurement, so there is no third-party taxonomy to license (CLAUDE.md's
/// licensing policy) and no chance of the anchors being tuned to the eval
/// queries they will later be judged against.
///
/// <para>
/// The rule, checked by hand against every category `seed/categories.sample.json`
/// sells while writing the file, and kept here rather than only in the
/// notebook because the reasoning belongs beside the type that reads the file:
/// <b>no outside category may overlap a category the shop sells.</b>
/// </para>
///
/// <list type="bullet">
/// <item>Sportswear and sports shoes ARE sold (<c>SPORTSWEAR</c>, <c>SHOES</c> —
/// running tees, trainers); balls, bikes and gym weights are not, so
/// <c>SPORTS_EQUIPMENT</c> is written to name equipment only, never apparel.</item>
/// <item>Small kitchen appliances ARE sold (<c>SMALL_APPLIANCE</c> — a grinder,
/// a kettle, a toaster, a blender, checked against every product of that type
/// in the seed); washing machines and fridges are not, so
/// <c>LARGE_APPLIANCES</c> names exactly the line the seed already draws.</item>
/// <item><b>Bedding, narrowed to mattresses.</b> The brief that named this
/// anchor called it <c>BEDDING_MATTRESSES</c>; checking it against the seed
/// found <c>TEXTILES</c> already sells a wool blanket and linen cushion
/// covers, which read as "bedding" in everyday speech. The anchor is
/// <c>MATTRESSES</c> instead — mattresses, bed frames, box springs — and
/// deliberately says nothing about sheets, duvets or cushions, which would
/// have overlapped what the shop already sells.</item>
/// <item><b>Phone cases, decided against <c>ACCESSORIES</c>.</b> Every product
/// the seed files under <c>ACCESSORIES</c> is an apparel complement — merino
/// socks, a wool scarf, a knitted beanie, touchscreen gloves — never
/// electronics. A phone case reads as an "accessory" in everyday English, but
/// it shares no taxonomy node with a scarf, so it is written into
/// <c>MOBILE_PHONES</c>'s own texts ("phone cases and chargers") rather than
/// treated as an overlap with the sold <c>ACCESSORIES</c> node.</item>
/// <item><b>Car accessories, for the same reason and labelled to say so.</b>
/// <c>CAR_ACCESSORIES</c> (mounts, seat covers, an air freshener) is a
/// different domain from <c>ACCESSORIES</c>, and its label reads "car
/// accessories" rather than bare "accessories" so a shopper reading the
/// refusal is not left wondering which one the shop means.</item>
/// <item><b>Tickets and travel, not luggage.</b> <c>LUGGAGE</c> is sold (a
/// cabin bag, a gym bag, a wash bag, packing cubes) and its own English name is
/// "Luggage and travel". <c>TICKETS_TRAVEL</c> is scoped strictly to bookings —
/// flights, hotels, event tickets — and never mentions a bag, so the two share
/// a word without sharing a product.</item>
/// </list>
/// </summary>
internal static class OutOfScopeAnchors
{
    private const string ResourceName = "ElGuerre.Tendero.Search.Answers.out-of-scope-anchors.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The committed categories, deserialized fresh on every call — this
    /// runs once per process at anchor-embedding time, not per query.</summary>
    public static OutOfScopeAnchorFile Load()
    {
        using var stream = OpenResource();

        return JsonSerializer.Deserialize<OutOfScopeAnchorFile>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{ResourceName}' is empty or not an out-of-scope anchor file.");
    }

    /// <summary>The raw bytes, for <see cref="ScopeFingerprint.OfBytes"/> — the
    /// fingerprint is over the file as committed, not over how it happens to
    /// deserialize, so a whitespace-only diff still changes it.</summary>
    public static byte[] Bytes()
    {
        using var stream = OpenResource();
        using var buffer = new MemoryStream();

        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static Stream OpenResource() =>
        typeof(OutOfScopeAnchors).Assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidDataException($"'{ResourceName}' was not compiled into the Search assembly.");
}

/// <summary>
/// The stand-in for <see cref="IQueryScope"/> until Task 10 wires a real
/// classifier, and the permanent stand-in on any process with no embedding
/// generator configured. Always <see cref="ScopeVerdict.Unknown"/>, and never
/// counted as a degradation: an unconfigured layer is not a failing one — the
/// same distinction <c>NoVectorLayer</c> already draws for the vector layer.
/// </summary>
internal sealed class NoQueryScope : IQueryScope
{
    public Task<ScopeVerdict> ClassifyAsync(string text, string culture, CancellationToken ct = default) =>
        Task.FromResult(ScopeVerdict.Unknown);
}
