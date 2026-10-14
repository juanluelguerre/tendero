using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.QueryDsl;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Offers;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Search.Elasticsearch;

// The facets of a listing (R-7.3, F-2), in four steps kept apart so each can be
// tested without a cluster: the filters a query chose (ProductFilters), the
// aggregations that count them (FacetAggregations), the counts read back
// (FacetCounts), and the facets a screen draws (FacetAssembler). The price bands
// are a pure function of their own (PriceBands).
//
// **The shape, once.** The shopper's filters narrow the HITS through
// `post_filter`, which aggregations do not see. Each facet is then counted by a
// `filter` aggregation holding every chosen filter EXCEPT its own — which is
// what makes a count disjunctive: choosing Vertex leaves Caminante's count what
// choosing it too would add. Every count is a `cardinality` of `productId`,
// because the index holds one document per variant and a shopper counts
// products.

/// <summary>The codes of the facets that are not attributes. Lower case, so they
/// can never meet an attribute's code, which the catalogue writes in upper case.</summary>
internal static class FacetCodes
{
    public const string Category = "category";
    public const string Brand = "brand";
    public const string Price = "price";
    public const string Offer = "offer";
    public const string New = "new";
    public const string InStock = "stock";

    /// <summary>A campaign's listing (R-8). Not a facet a screen draws: a filter
    /// every facet is narrowed by, because the listing IS the campaign.</summary>
    public const string Campaign = "campaign";
}

/// <summary>One filter the query chose, and the facet it belongs to — the facet
/// whose own counts must not be narrowed by it.</summary>
internal sealed record ChosenFilter(string Facet, Query Query);

/// <summary>
/// The filters of one query, as Elasticsearch clauses, each tagged with its facet.
///
/// The three switches carry their meaning whether or not they were chosen: the
/// facet for "on offer" counts what switching it on would leave, so it needs the
/// clause either way.
/// </summary>
internal sealed class ProductFilters
{
    private ProductFilters(
        SearchFilters asked, IReadOnlyList<ChosenFilter> chosen, Query onOffer, Query newArrival, Query available)
    {
        Asked = asked;
        Chosen = chosen;
        OnOffer = onOffer;
        NewArrival = newArrival;
        Available = available;
    }

    public SearchFilters Asked { get; }
    public IReadOnlyList<ChosenFilter> Chosen { get; }
    public Query OnOffer { get; }
    public Query NewArrival { get; }
    public Query Available { get; }

    /// <summary>The attribute and axis codes chosen, in the order first asked.</summary>
    public IReadOnlyList<string> ChosenCodes => [.. Chosen.Select(filter => filter.Facet).Where(IsAttributeCode).Distinct()];

    /// <param name="campaign">What the asked campaign's promotions reach now; null reads as nothing,
    /// so a campaign asked for and not looked up can never widen a listing to the whole shop.</param>
    public static ProductFilters From(SearchFilters? filters, OfferScope offers, DateOnly today, OfferScope? campaign = null)
    {
        var asked = filters ?? SearchFilters.None;
        var onOffer = OfferQuery(offers);
        var newArrival = NewArrivalQuery(today);
        var available = new TermQuery(Infer.Field<ProductSearchDocument>(d => d.InStock), FieldValue.True);

        var chosen = new List<ChosenFilter>();

        if (asked.Within.Count > 0)
            chosen.Add(new(FacetCodes.Category, AnyOf(Infer.Field<ProductSearchDocument>(d => d.CategoryCodes), asked.Within)));

        if (asked.Brands.Count > 0)
            chosen.Add(new(FacetCodes.Brand, AnyOf(BrandKeyword, asked.Brands)));

        if (asked.MinPrice is not null || asked.MaxPrice is not null)
            chosen.Add(new(FacetCodes.Price, PriceBetween(asked.MinPrice, asked.MaxPrice)));

        if (asked.OnOffer)
            chosen.Add(new(FacetCodes.Offer, onOffer));

        if (asked.NewArrivals)
            chosen.Add(new(FacetCodes.New, newArrival));

        if (asked.InStock)
            chosen.Add(new(FacetCodes.InStock, available));

        // The same clause as "on offer", over the campaign's own reach: one rule
        // for what an offer reaches, whichever promotions it is asked of.
        if (asked.Campaign is not null)
            chosen.Add(new(FacetCodes.Campaign, OfferQuery(campaign ?? OfferScope.Nothing)));

        // One group per CODE, its values OR'd. Axes live on the variant document
        // and attributes on every document of the product, and a "CODE:VALUE" is
        // unique across both, so one group reads both fields: "COLOR:BLACK" and
        // "SIZE:42" are then two filters on the SAME document, which is what makes
        // a black 42 exact.
        foreach (var group in asked.Facets
                     .Select(pair => (Code: FacetValues.CodeOf(pair), Pair: pair))
                     .Where(pair => pair.Code is not null)
                     .GroupBy(pair => pair.Code!, StringComparer.Ordinal))
        {
            string[] values = [.. group.Select(pair => pair.Pair).Distinct(StringComparer.Ordinal)];

            chosen.Add(new(group.Key, new BoolQuery
            {
                Should =
                [
                    AnyOf(Infer.Field<ProductSearchDocument>(d => d.AxisValues), values),
                    AnyOf(Infer.Field<ProductSearchDocument>(d => d.FacetValues), values),
                ],
                MinimumShouldMatch = 1,
            }));
        }

        return new ProductFilters(asked, chosen, onOffer, newArrival, available);
    }

    /// <summary>Every chosen filter, for the hits; null when nothing was chosen.</summary>
    public Query? All() => Chosen.Count == 0 ? null : Conjunction(Chosen);

    /// <summary>Every chosen filter but the facet's own — what its counts are narrowed by.</summary>
    public Query AllExcept(string facet) => Conjunction([.. Chosen.Where(filter => filter.Facet != facet)]);

    /// <summary>Which facets narrow <paramref name="facet"/>'s counts: the test's view of <see cref="AllExcept"/>.</summary>
    public IReadOnlyList<string> Narrowing(string facet) =>
        [.. Chosen.Where(filter => filter.Facet != facet).Select(filter => filter.Facet)];

    public static bool IsAttributeCode(string facet) => facet.Length > 0 && !char.IsLower(facet[0]);

    internal static Field BrandKeyword => Infer.Field<ProductSearchDocument>(d => d.Brand!.Suffix("raw"));

    internal static Query PriceBetween(decimal? from, decimal? to) =>
        new NumberRangeQuery(Infer.Field<ProductSearchDocument>(d => d.PriceAmount))
        {
            Gte = from is { } lower ? (double)lower : null,
            Lte = to is { } upper ? (double)upper : null,
        };

    private static Query Conjunction(IReadOnlyList<ChosenFilter> filters) =>
        filters.Count == 0
            ? new MatchAllQuery()
            : new BoolQuery { Filter = [.. filters.Select(filter => filter.Query)] };

    private static Query AnyOf(Field field, IEnumerable<string> values) =>
        new TermsQuery(field, new TermsQueryField([.. values.Select(FieldValue.String)]));

    /// <summary>
    /// What the running offers reach: a branch through <c>categoryCodes</c> (the
    /// whole ancestry is there, so a promotion on APPAREL reaches a shirt filed
    /// under SHIRT) or a SKU. Everything is every product; nothing is none.
    /// </summary>
    private static Query OfferQuery(OfferScope offers)
    {
        if (offers.Everything)
            return new MatchAllQuery();

        List<Query> reached = [];
        if (offers.CategoryCodes.Count > 0)
            reached.Add(AnyOf(Infer.Field<ProductSearchDocument>(d => d.CategoryCodes), offers.CategoryCodes));
        if (offers.Skus.Count > 0)
            reached.Add(AnyOf(Infer.Field<ProductSearchDocument>(d => d.Sku), offers.Skus));

        return reached.Count == 0
            ? new MatchNoneQuery()
            : new BoolQuery { Should = reached, MinimumShouldMatch = 1 };
    }

    /// <summary>
    /// Listed in the last thirty days, as a range computed when asked. "New" is a
    /// fact that changes with time and not with an event, so it is never stored as
    /// a flag (the spec's "Why on offer is not in the index").
    /// </summary>
    private static Query NewArrivalQuery(DateOnly today) =>
        new DateRangeQuery(Infer.Field<ProductSearchDocument>(d => d.AvailableFrom!))
        {
            Gte = DateMath.Anchored(today.AddDays(-SearchFilters.NewArrivalDays).ToDateTime(TimeOnly.MinValue)),
            Lte = DateMath.Anchored(today.ToDateTime(TimeOnly.MinValue)),
        };
}

/// <summary>The "CODE:VALUE" keywords of axes and attributes.</summary>
internal static class FacetValues
{
    public static string? CodeOf(string pair) =>
        pair.IndexOf(':') is var colon and > 0 && colon < pair.Length - 1 ? pair[..colon] : null;

    public static string ValueOf(string pair) => pair[(pair.IndexOf(':') + 1)..];
}

/// <summary>
/// The aggregations that count the facets. One request answers the page and
/// every count; only the price bands need a second (<see cref="PriceBandCounts"/>).
/// </summary>
internal static class FacetAggregations
{
    /// <summary>
    /// Below this many distinct products per bucket a cardinality is counted in a
    /// hash set rather than estimated, so at laboratory scale — a hundred products
    /// — every count is exact. It is also Elasticsearch's default, written down
    /// because the counts depend on it: the day a bucket holds more than 3,000
    /// products its count becomes an estimate within a few percent.
    /// </summary>
    public const int PrecisionThreshold = 3000;

    /// <summary>
    /// Buckets per facet. Generous on purpose: every brand and every category of a
    /// catalogue far past this one, so no count is ever cut; the screen, not the
    /// server, decides that seven brands show and the rest wait behind "see more".
    /// </summary>
    public const int OptionsPerFacet = 100;

    /// <summary>
    /// Every axis and attribute value in one terms aggregation, for the codes
    /// nobody chose. Today that is a few dozen values; a catalogue with more than
    /// this many distinct ones needs one aggregation per attribute, which is the
    /// shape the chosen codes already use.
    /// </summary>
    public const int ValuesVocabulary = 1000;

    /// <summary>The percentiles the price bands are cut from (<see cref="PriceBands"/>).</summary>
    public static readonly double[] BandPercents = [20, 40, 60, 80];

    public const string Products = "products";
    public const string Total = "total";
    public const string Values = "values";
    public const string Axes = "axes";
    public const string Attributes = "attributes";
    public const string Switch = "on";
    public const string Min = "min";
    public const string Max = "max";
    public const string Percentiles = "percentiles";
    public const string Bands = "bands";

    /// <summary>The aggregation counting the values of a code somebody chose, by its position.</summary>
    public static string ValuesOf(int position) => $"{Values}_{position}";

    public static Dictionary<string, Aggregation> For(ProductFilters filters)
    {
        var aggregations = new Dictionary<string, Aggregation>
        {
            // The number the page shows: every chosen filter, as the hits have.
            [Total] = Narrowed(filters.All() ?? new MatchAllQuery()),

            [FacetCodes.Category] = Narrowed(filters.AllExcept(FacetCodes.Category),
                (Values, Terms(Infer.Field<ProductSearchDocument>(d => d.CategoryCodes), OptionsPerFacet))),

            [FacetCodes.Brand] = Narrowed(filters.AllExcept(FacetCodes.Brand),
                (Values, Terms(ProductFilters.BrandKeyword, OptionsPerFacet))),

            // Per variant document: the bands are cut from the prices on offer,
            // and a variant's price is one of them.
            [FacetCodes.Price] = Narrowed(filters.AllExcept(FacetCodes.Price),
                (Min, new Aggregation { Min = new MinAggregation { Field = Infer.Field<ProductSearchDocument>(d => d.PriceAmount) } }),
                (Max, new Aggregation { Max = new MaxAggregation { Field = Infer.Field<ProductSearchDocument>(d => d.PriceAmount) } }),
                (Percentiles, new Aggregation
                {
                    Percentiles = new PercentilesAggregation
                    {
                        Field = Infer.Field<ProductSearchDocument>(d => d.PriceAmount),
                        Percents = BandPercents,
                    },
                })),

            [FacetCodes.Offer] = Narrowed(filters.AllExcept(FacetCodes.Offer), (Switch, Narrowed(filters.OnOffer))),
            [FacetCodes.New] = Narrowed(filters.AllExcept(FacetCodes.New), (Switch, Narrowed(filters.NewArrival))),
            [FacetCodes.InStock] = Narrowed(filters.AllExcept(FacetCodes.InStock), (Switch, Narrowed(filters.Available))),

            // The codes nobody chose share one count, under every chosen filter.
            [Values] = Narrowed(filters.All() ?? new MatchAllQuery(),
                (Axes, Terms(Infer.Field<ProductSearchDocument>(d => d.AxisValues), ValuesVocabulary)),
                (Attributes, Terms(Infer.Field<ProductSearchDocument>(d => d.FacetValues), ValuesVocabulary))),
        };

        // A chosen code is counted without its own group, which one aggregation
        // for all of them could not do.
        var codes = filters.ChosenCodes;
        for (var position = 0; position < codes.Count; position++)
        {
            var prefix = new TermsInclude(PrefixPattern(codes[position]));

            aggregations[ValuesOf(position)] = Narrowed(filters.AllExcept(codes[position]),
                (Axes, Terms(Infer.Field<ProductSearchDocument>(d => d.AxisValues), OptionsPerFacet, prefix)),
                (Attributes, Terms(Infer.Field<ProductSearchDocument>(d => d.FacetValues), OptionsPerFacet, prefix)));
        }

        return aggregations;
    }

    /// <summary>
    /// The count of each price band, which needs the bands first — and they are
    /// cut from the percentiles the first request returned. A second request,
    /// with no hits, rather than fixed bands in the first: fixed bands would put
    /// every desk lamp in one and every pan in another, and bands exist to split
    /// THIS result set. It carries the same query and every filter but price.
    /// </summary>
    public static Dictionary<string, Aggregation> PriceBandCounts(ProductFilters filters, IReadOnlyList<(decimal? From, decimal? To)> bands) =>
        new()
        {
            [Bands] = Narrowed(filters.AllExcept(FacetCodes.Price),
                (Bands, new Aggregation
                {
                    Filters = new FiltersAggregation
                    {
                        Filters = new Buckets<Query>(bands
                            .Select((band, position) => (band, position))
                            .ToDictionary(
                                pair => pair.position.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                pair => ProductFilters.PriceBetween(pair.band.From, pair.band.To))),
                    },
                    Aggregations = new Dictionary<string, Aggregation> { [Products] = CountProducts() },
                })),
        };

    /// <summary>A filter aggregation counting products, with whatever else is asked inside it.</summary>
    private static Aggregation Narrowed(Query filter, params (string Name, Aggregation Aggregation)[] inside)
    {
        var aggregations = new Dictionary<string, Aggregation> { [Products] = CountProducts() };
        foreach (var (name, aggregation) in inside)
            aggregations[name] = aggregation;

        return new Aggregation { Filter = filter, Aggregations = aggregations };
    }

    private static Aggregation Terms(Field field, int size, TermsInclude? include = null) => new()
    {
        Terms = new TermsAggregation { Field = field, Size = size, Include = include },
        Aggregations = new Dictionary<string, Aggregation> { [Products] = CountProducts() },
    };

    private static Aggregation CountProducts() => new()
    {
        Cardinality = new CardinalityAggregation
        {
            Field = Infer.Field<ProductSearchDocument>(d => d.ProductId),
            PrecisionThreshold = PrecisionThreshold,
        },
    };

    /// <summary>
    /// "CODE:" and anything after it, as a Lucene regular expression. Every
    /// character that is not a letter or a digit is escaped, so a code is always
    /// read literally.
    /// </summary>
    internal static string PrefixPattern(string code) =>
        string.Concat(code.Select(character => char.IsAsciiLetterOrDigit(character) ? character.ToString() : $"\\{character}")) + ":.*";
}

/// <summary>A facet's counts before it is labelled: how many products it would
/// start from, and how many each value leaves.</summary>
internal sealed record OptionCounts(long Base, IReadOnlyDictionary<string, long> Counts)
{
    public static readonly OptionCounts Empty = new(0, new Dictionary<string, long>());
}

/// <summary>A switch's counts: products before it, and products with it on.</summary>
internal sealed record SwitchCounts(long Base, long On);

/// <summary>The spread of prices the bands are cut from, as the first request saw it.</summary>
internal sealed record PriceSpread(decimal Min, decimal Max, IReadOnlyList<decimal> Percentiles);

/// <summary>One code's values, and whether they came from the variant's axes.</summary>
internal sealed record CodeCounts(bool IsAxis, OptionCounts Options);

internal sealed record FacetCounts(
    long Total,
    OptionCounts Categories,
    OptionCounts Brands,
    PriceSpread? Price,
    SwitchCounts Offer,
    SwitchCounts New,
    SwitchCounts InStock,
    IReadOnlyDictionary<string, CodeCounts> Codes)
{
    /// <summary>Reads the counts back from the aggregations <see cref="FacetAggregations.For"/> asked for.</summary>
    public static FacetCounts Read(AggregateDictionary? aggregations, ProductFilters filters)
    {
        var total = ProductsIn(aggregations?.GetFilter(FacetAggregations.Total)?.Aggregations);

        var codes = new Dictionary<string, CodeCounts>(StringComparer.Ordinal);
        var chosen = filters.ChosenCodes;

        // The codes nobody chose, from the shared count...
        foreach (var (code, counts) in CodesIn(aggregations?.GetFilter(FacetAggregations.Values), total))
            if (!chosen.Contains(code, StringComparer.Ordinal))
                codes[code] = counts;

        // ...and each chosen one from its own, counted without itself.
        for (var position = 0; position < chosen.Count; position++)
        {
            var own = aggregations?.GetFilter(FacetAggregations.ValuesOf(position));
            var counts = CodesIn(own, ProductsIn(own?.Aggregations)).GetValueOrDefault(chosen[position]);
            codes[chosen[position]] = counts ?? new CodeCounts(false, new OptionCounts(ProductsIn(own?.Aggregations), new Dictionary<string, long>()));
        }

        return new FacetCounts(
            total,
            OptionsIn(aggregations?.GetFilter(FacetCodes.Category)),
            OptionsIn(aggregations?.GetFilter(FacetCodes.Brand)),
            SpreadIn(aggregations?.GetFilter(FacetCodes.Price)),
            SwitchIn(aggregations?.GetFilter(FacetCodes.Offer)),
            SwitchIn(aggregations?.GetFilter(FacetCodes.New)),
            SwitchIn(aggregations?.GetFilter(FacetCodes.InStock)),
            codes);
    }

    public static long ProductsIn(AggregateDictionary? aggregations) =>
        aggregations?.GetCardinality(FacetAggregations.Products)?.Value ?? 0;

    private static OptionCounts OptionsIn(FilterAggregate? facet) =>
        facet is null
            ? OptionCounts.Empty
            : new OptionCounts(ProductsIn(facet.Aggregations), BucketsIn(facet.Aggregations?.GetStringTerms(FacetAggregations.Values)));

    private static SwitchCounts SwitchIn(FilterAggregate? facet) =>
        new(ProductsIn(facet?.Aggregations), ProductsIn(facet?.Aggregations?.GetFilter(FacetAggregations.Switch)?.Aggregations));

    private static PriceSpread? SpreadIn(FilterAggregate? facet)
    {
        if (facet?.Aggregations?.GetMin(FacetAggregations.Min)?.Value is not { } min
            || facet.Aggregations.GetMax(FacetAggregations.Max)?.Value is not { } max)
            return null;

        var percentiles = facet.Aggregations.GetTDigestPercentiles(FacetAggregations.Percentiles)?.Values?
            .Where(item => item.Value is not null)
            .Select(item => Money(item.Value!.Value))
            .ToArray() ?? [];

        return new PriceSpread(Money(min), Money(max), percentiles);
    }

    private static Dictionary<string, CodeCounts> CodesIn(FilterAggregate? facet, long @base)
    {
        var codes = new Dictionary<string, CodeCounts>(StringComparer.Ordinal);
        if (facet is null)
            return codes;

        foreach (var (field, isAxis) in new[] { (FacetAggregations.Axes, true), (FacetAggregations.Attributes, false) })
        {
            foreach (var (pair, count) in BucketsIn(facet.Aggregations?.GetStringTerms(field)))
            {
                if (FacetValues.CodeOf(pair) is not { } code)
                    continue;

                // Summed, not replaced: an axis value reaches the index per variant
                // (AxisValues) for a product with variants along it, and per product
                // (FacetValues) for one without — never both for one product, so the
                // two fields count disjoint products and their sum is exact.
                var existing = codes.GetValueOrDefault(code);
                var counts = new Dictionary<string, long>(existing?.Options.Counts ?? new Dictionary<string, long>(), StringComparer.Ordinal);
                counts[pair] = counts.GetValueOrDefault(pair) + count;
                codes[code] = new CodeCounts(existing?.IsAxis ?? isAxis, new OptionCounts(@base, counts));
            }
        }

        return codes;
    }

    private static Dictionary<string, long> BucketsIn(StringTermsAggregate? terms) =>
        terms?.Buckets.ToDictionary(
            bucket => bucket.Key.TryGetString(out var key) ? key : bucket.Key.ToString(),
            bucket => ProductsIn(bucket.Aggregations),
            StringComparer.Ordinal)
        ?? [];

    /// <summary>Prices are indexed as doubles; they come back as the cents they were.</summary>
    private static decimal Money(double value) => Math.Round((decimal)value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// The price bands (the spec's open question 1, decided: as Amazon). Three to
/// five, cut at the results' percentiles and rounded to prices a person would
/// say. A pure function, so every edge is a unit test rather than a cluster.
/// </summary>
internal static class PriceBands
{
    public const int MaxBands = 5;

    /// <summary>
    /// The mantissas of a price a person says: 10, 15, 20, 25, 30, 40, 50, 60,
    /// 75, 100 and on up by tens. Nobody says "37".
    /// </summary>
    private static readonly decimal[] Mantissas = [1m, 1.5m, 2m, 2.5m, 3m, 4m, 5m, 6m, 7.5m];

    /// <summary>
    /// The prices the bands meet at, strictly inside the range, ascending, at
    /// most four. Cut at the percentiles so each band holds a similar share of
    /// the results; when those collapse into fewer than two distinct prices — a
    /// narrow or lopsided range — the quarters of the range are tried as well,
    /// and when even they give none (every price rounds to the same one) there
    /// are no bands, only the two inputs.
    /// </summary>
    public static IReadOnlyList<decimal> Boundaries(decimal min, decimal max, IEnumerable<decimal> percentiles)
    {
        if (max <= min)
            return [];

        var cuts = Inside(min, max, percentiles.Select(Round));
        if (cuts.Count < 2)
            cuts = Inside(min, max, [.. cuts, .. Enumerable.Range(1, 3).Select(quarter => Round(min + (max - min) * quarter / 4))]);

        if (cuts.Count <= MaxBands - 1)
            return cuts;

        // More than four: keep four spread across them, the ends included.
        return [.. Enumerable.Range(0, MaxBands - 1).Select(index => cuts[index * (cuts.Count - 1) / (MaxBands - 2)]).Distinct()];
    }

    /// <summary>The bands between the boundaries: the lowest open below, the highest open above.</summary>
    public static IReadOnlyList<(decimal? From, decimal? To)> Between(IReadOnlyList<decimal> boundaries)
    {
        if (boundaries.Count == 0)
            return [];

        var bands = new List<(decimal? From, decimal? To)> { (null, boundaries[0]) };
        for (var index = 1; index < boundaries.Count; index++)
            bands.Add((boundaries[index - 1], boundaries[index]));
        bands.Add((boundaries[^1], null));

        return bands;
    }

    /// <summary>The nearest price a person would say; halfway goes down.</summary>
    public static decimal Round(decimal price)
    {
        if (price <= 0)
            return 0;

        var magnitude = 1m;
        while (magnitude * 10 <= price)
            magnitude *= 10;
        while (magnitude > price)
            magnitude /= 10;

        return Mantissas
            .Select(mantissa => mantissa * magnitude)
            .Append(magnitude * 10)
            .OrderBy(candidate => Math.Abs(candidate - price))
            .ThenBy(candidate => candidate)
            .First();
    }

    private static List<decimal> Inside(decimal min, decimal max, IEnumerable<decimal> candidates) =>
        [.. candidates.Where(cut => cut > min && cut < max).Distinct().Order()];
}

/// <summary>
/// Whether a facet earns its place: **it is shown only when it splits the
/// results** (the spec). The server's rule, so every screen and every agent
/// agrees on it.
/// </summary>
internal static class FacetRules
{
    /// <summary>
    /// Options split the results when at least two of them hold products AND at
    /// least one would leave fewer products than the facet starts from. The
    /// second half is what a multi-valued field needs: every product in a leaf
    /// carries its department and its parent too, so a search inside one leaf
    /// has three category codes that each hold every result, and a range of
    /// shoes that all come in every size has five sizes that narrow nothing.
    /// For one value per product — a brand — the two halves say the same thing.
    ///
    /// Something chosen always shows its facet, or it could not be unchosen.
    /// </summary>
    public static bool OptionsSplit(OptionCounts counts, bool anyChosen)
    {
        if (anyChosen)
            return true;

        var holding = counts.Counts.Values.Where(count => count > 0).ToArray();
        return holding.Length >= 2 && holding.Any(count => count < counts.Base);
    }

    /// <summary>
    /// A switch is shown when turning it on would narrow the results — some
    /// products have it and some do not — or when it is on. Found on the
    /// laboratory's shoes (2026-09-28): every one was on offer and in stock, and
    /// two switches that could only return the same thirteen products sat in the
    /// column doing nothing. The options' "narrows" half, applied to a switch.
    /// </summary>
    public static bool SwitchShows(SwitchCounts counts, bool chosen) =>
        chosen || counts.On > 0 && counts.On < counts.Base;

    /// <summary>A price range is shown when the results hold two prices or more, or when one was chosen.</summary>
    public static bool PriceShows(PriceSpread? spread, bool chosen) => chosen || spread is { } range && range.Min < range.Max;
}

/// <summary>
/// The counts turned into what a column draws: labelled in the query's
/// culture, in the spec's order, and only the facets that split the results.
/// </summary>
internal static class FacetAssembler
{
    private static readonly LocalizedText CategoryLabel = Labels("Categoría", "Category");
    private static readonly LocalizedText BrandLabel = Labels("Marca", "Brand");
    private static readonly LocalizedText PriceLabel = Labels("Precio", "Price");
    private static readonly LocalizedText OfferLabel = Labels("En oferta", "On offer");
    private static readonly LocalizedText NewLabel = Labels("Novedades", "New arrivals");
    private static readonly LocalizedText InStockLabel = Labels("Disponible", "Available");

    /// <summary>What a built-in switch's one option carries back to the URL.</summary>
    public const string SwitchValue = "true";

    public static IReadOnlyList<SearchFacet> Build(
        FacetCounts counts,
        IReadOnlyList<PriceBand> bands,
        ProductFilters filters,
        string culture,
        AttributeDefinitions definitions,
        CategoryTree categories,
        string currency = ShopOptions.DefaultCurrency)
    {
        var asked = filters.Asked;
        Money? In(decimal? amount) => amount is { } value ? new Money(value, currency) : null;
        var facets = new List<SearchFacet>();

        AddOptions(facets, FacetCodes.Category, FacetKind.Category, CategoryLabel.In(culture), counts.Categories, asked.Within,
            code => categories.ByCode(code)?.Name.In(culture) ?? code,
            options => options.OrderByDescending(option => option.Count).ThenBy(option => option.Value, StringComparer.Ordinal));

        AddOptions(facets, FacetCodes.Brand, FacetKind.Brand, BrandLabel.In(culture), counts.Brands, asked.Brands,
            brand => brand,
            options => options.OrderByDescending(option => option.Count).ThenBy(option => option.Label, StringComparer.OrdinalIgnoreCase));

        var priceChosen = asked.MinPrice is not null || asked.MaxPrice is not null;
        if (FacetRules.PriceShows(counts.Price, priceChosen))
        {
            facets.Add(new SearchFacet(FacetCodes.Price, FacetKind.Price, PriceLabel.In(culture), [],
                new PriceFacetRange(
                    new Money(counts.Price?.Min ?? asked.MinPrice ?? 0, currency),
                    new Money(counts.Price?.Max ?? asked.MaxPrice ?? 0, currency),
                    In(asked.MinPrice),
                    In(asked.MaxPrice),
                    bands)));
        }

        AddSwitch(facets, FacetCodes.Offer, FacetKind.Offer, OfferLabel.In(culture), counts.Offer, asked.OnOffer);
        AddSwitch(facets, FacetCodes.New, FacetKind.New, NewLabel.In(culture), counts.New, asked.NewArrivals);
        AddSwitch(facets, FacetCodes.InStock, FacetKind.InStock, InStockLabel.In(culture), counts.InStock, asked.InStock);

        foreach (var (code, codeCounts) in counts.Codes
                     .OrderBy(pair => Position(definitions, pair.Key))
                     .ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var definition = definitions.ByCode(code);
            var label = definition?.Label.In(culture) ?? code;
            var chosen = asked.Facets.Where(pair => FacetValues.CodeOf(pair) == code).ToArray();

            if (definition is { Kind: AttributeKind.Boolean })
            {
                // A yes-or-no is a switch: its one option is "yes".
                var yes = $"{code}:true";
                var on = codeCounts.Options.Counts.GetValueOrDefault(yes);
                var isOn = chosen.Contains(yes, StringComparer.Ordinal);

                if (FacetRules.SwitchShows(new SwitchCounts(codeCounts.Options.Base, on), isOn || chosen.Length > 0))
                    facets.Add(new SearchFacet(code, FacetKind.Flag, label, [new FacetOption(yes, label, on, isOn)]));

                continue;
            }

            var kind = (definition?.IsVariantAxis ?? codeCounts.IsAxis) ? FacetKind.Axis : FacetKind.Attribute;
            var order = definition?.Options.Select(option => $"{code}:{option.Code}").ToList() ?? [];

            AddOptions(facets, code, kind, label, codeCounts.Options, chosen,
                pair => definition?.Options.FirstOrDefault(option =>
                            string.Equals(option.Code, FacetValues.ValueOf(pair), StringComparison.OrdinalIgnoreCase))?.Label.In(culture)
                        ?? FacetValues.ValueOf(pair),
                options => options
                    .OrderBy(option => order.IndexOf(option.Value) is var index and >= 0 ? index : int.MaxValue)
                    .ThenBy(option => option.Value, StringComparer.Ordinal),
                pair => definition?.Options.FirstOrDefault(option =>
                            string.Equals(option.Code, FacetValues.ValueOf(pair), StringComparison.OrdinalIgnoreCase))?.Swatch);
        }

        return facets;
    }

    /// <summary>The bands with their counts, the empty ones dropped unless chosen.</summary>
    public static IReadOnlyList<PriceBand> Bands(
        IReadOnlyList<(decimal? From, decimal? To)> bands, IReadOnlyList<long> counts, SearchFilters asked,
        string currency = ShopOptions.DefaultCurrency) =>
    [
        .. bands
            .Select((band, position) => new PriceBand(
                band.From is { } from ? new Money(from, currency) : null,
                band.To is { } to ? new Money(to, currency) : null,
                position < counts.Count ? counts[position] : 0,
                band.From == asked.MinPrice && band.To == asked.MaxPrice))
            .Where(band => band.Count > 0 || band.Chosen)
    ];

    private static void AddOptions(
        List<SearchFacet> facets, string code, FacetKind kind, string label, OptionCounts counts,
        IReadOnlyCollection<string> chosen, Func<string, string> labelOf,
        Func<IEnumerable<FacetOption>, IEnumerable<FacetOption>> order, Func<string, string?>? swatchOf = null)
    {
        if (!FacetRules.OptionsSplit(counts, chosen.Count > 0))
            return;

        var values = counts.Counts
            .Where(pair => pair.Value > 0)
            .Select(pair => pair.Key)
            .Union(chosen, StringComparer.Ordinal);

        var options = values.Select(value => new FacetOption(
            value, labelOf(value), counts.Counts.GetValueOrDefault(value), chosen.Contains(value, StringComparer.Ordinal), swatchOf?.Invoke(value)));

        facets.Add(new SearchFacet(code, kind, label, [.. order(options)]));
    }

    private static void AddSwitch(List<SearchFacet> facets, string code, FacetKind kind, string label, SwitchCounts counts, bool chosen)
    {
        if (FacetRules.SwitchShows(counts, chosen))
            facets.Add(new SearchFacet(code, kind, label, [new FacetOption(SwitchValue, label, counts.On, chosen)]));
    }

    /// <summary>The definitions' order, which is the order the shopkeeper gave them; unknown codes after.</summary>
    private static int Position(AttributeDefinitions definitions, string code)
    {
        for (var index = 0; index < definitions.All.Count; index++)
            if (string.Equals(definitions.All[index].Code, code, StringComparison.OrdinalIgnoreCase))
                return index;

        return int.MaxValue;
    }

    private static LocalizedText Labels(string es, string en) =>
        new(new Dictionary<string, string> { ["es"] = es, ["en"] = en });
}
