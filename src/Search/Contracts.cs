using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Search.Contracts;

/// <summary>
/// The write port: projecting products into the search engine.
/// Elasticsearch is an infrastructure detail behind this interface.
/// </summary>
public interface IProductIndexer
{
    Task IndexAsync(Product product, CancellationToken ct = default);
    Task RemoveAsync(ProductId productId, CancellationToken ct = default);
}

/// <summary>
/// Reading the catalogue from the search side. It lived inside the
/// ProjectProductToIndex slice while that was its only reader; with
/// ReindexProducts it became shared, and a slice cannot reference another slice.
///
/// <c>StreamAllAsync</c> returns ALL products, not only the Active ones: the
/// reindex applies the same rule as the projection (Active is indexed, the rest
/// is removed), and that is what makes the index converge on the truth instead
/// of merely adding to it. Filtering by Active here would leave stale documents
/// behind for whatever stopped being Active.
/// </summary>
public interface IProductReader
{
    /// <summary>
    /// The product a SKU belongs to. Inventory raises its events with a SKU
    /// because that is the only vocabulary it has; turning one into a product is
    /// Search's job, since Search is the only thing that reads both.
    /// </summary>
    Task<Product?> FindBySkuAsync(string sku, CancellationToken ct = default);

    Task<Product?> GetByIdAsync(ProductId id, CancellationToken ct);
    IAsyncEnumerable<Product> StreamAllAsync(CancellationToken ct);
}

/// <summary>
/// The cultures the search serves, and the order they are walked in. This is
/// CONTRACT: the evaluation tool needs to know which indexes to score against,
/// and the initialiser needs to know which ones to create.
///
/// It lived next to the analyser map inside the Elasticsearch adapter, and those
/// are two different things: that the catalogue speaks Spanish and English is a
/// product decision; that "es" is analysed with the <c>spanish</c> stemmer is an
/// engine detail, and that one stays inside the adapter.
/// </summary>
public static class SearchCultures
{
    public static readonly string[] Supported = ["es", "en"];

    public static IEnumerable<string> IndexNames =>
        Supported.Select(ProductSearchDocument.IndexNameFor);
}

/// <summary>
/// The index's rule, in one place: only Active is searchable, and whatever stops
/// being Active is removed. It was written twice — in the outbox projection and
/// in the reindex — under a comment saying "replays the rule rather than
/// inventing another", which is the polite way of saying there are two.
///
/// That there were two matters: if they diverge, the reindex stops converging on
/// what the projection produces, and the difference is only visible by searching.
/// </summary>
public static class ProductIndexProjection
{
    /// <summary>True when the product ended up indexed, false when it was removed.</summary>
    public static async Task<bool> ApplyAsync(
        IProductIndexer indexer, Product product, CancellationToken ct)
    {
        if (product.Status == ProductStatus.Active)
        {
            await indexer.IndexAsync(product, ct);
            return true;
        }

        await indexer.RemoveAsync(product.Id, ct);
        return false;
    }
}

/// <summary>
/// The lexical read port (BM25). When hybrid search arrives there will be
/// another port composing this one with the vector side; this one does NOT
/// change: it is the degraded mode when the AI layer goes down.
/// </summary>
public interface ILexicalProductSearch
{
    Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default);
}

/// <summary>
/// The search engine could not answer. Its own type and not an
/// InvalidOperationException, because the API has to tell them apart: an engine
/// failure is a 503 and gets retried, not a 500 that suggests a programming
/// error.
///
/// <paramref name="diagnostics"/> is the Elastic client's audit trail, which is
/// long and contains internal paths and ports: it is kept because it is what
/// makes diagnosis quick in the log, and for that same reason it must never end
/// up in an HTTP response body.
/// </summary>
public sealed class SearchUnavailableException(string operation, string diagnostics)
    : Exception($"Search backend failed during {operation}. {diagnostics}")
{
    public string Operation { get; } = operation;
}

/// <param name="Text">
/// What was typed, or empty to BROWSE.
///
/// An empty query used to be a validation failure, which meant the only way into
/// the catalogue was to already know what you wanted. A shop is not a search
/// engine with products behind it: somebody who has just arrived has nothing to
/// type, and every row on a home page — a category, the new arrivals, what is on
/// offer — is the same question with no words in it.
/// </param>
/// <param name="Category">
/// A category code, matched against the whole BRANCH rather than the leaf, so
/// "Cocina" returns what is inside it. It is the same call the index already
/// made when it chose to store the materialised path as analysed text.
/// </param>
/// <param name="Sort">
/// `newest` orders by the source's first-listed date; anything else, including
/// null, leaves relevance in charge — which is the right default and the only
/// one that means anything when there IS a query.
/// </param>
public sealed record ProductSearchQuery(
    string Text, string Culture, int Page = 1, int PageSize = 20,
    string? Category = null, string? Sort = null);

/// <summary>
/// A result is a PRODUCT, even though what matched was one specific variant
/// (ADR 0015). That is why it carries both: the product, which is what gets
/// shown, and the variant that won, which is the one the picker should bring up
/// already selected and the one an agent can add to a cart without a second
/// call.
/// </summary>
public sealed record SearchHit(
    string ProductId,
    string Code,
    string Name,
    string Slug,
    string? Brand,
    string? Category,
    string MatchedVariantId,
    string MatchedSku,
    decimal PriceAmount,
    decimal PriceFrom,
    decimal PriceTo,
    string PriceCurrency,
    string? ImageId,
    /// <summary>
    /// Whether the variant that matched can be bought right now.
    ///
    /// It is the WINNING VARIANT's availability, not the product's, and that is
    /// the whole reason the variant is the indexed unit: "some variant of this
    /// is in stock" is how a card says available and the size you want is not
    /// (ADR 0015).
    /// </summary>
    bool InStock,
    double Score);

public sealed record SearchResultPage(
    IReadOnlyList<SearchHit> Hits,
    long Total,
    int Page,
    int PageSize,
    double TookMs);

/// <summary>
/// A flat document per (VARIANT, culture), collapsed to a product at query time.
///
/// Indexing per variant is what makes the filters exact: with one document per
/// product, `colour=navy AND size=38` matches a product that has navy in a 40
/// and black in a 38, and the customer arrives at a combination that does not
/// exist. Price and stock stop being ranges and go back to being facts.
///
/// Collapsing on `ProductId` at query time is what avoids the other extreme:
/// without it a product with eight variants floods the top ten, the golden set —
/// which is annotated per product — stops being comparable, and the vector
/// ranking, which is per product because variants share a name and a
/// description, no longer fuses with the lexical one.
///
/// The product's price range travels DUPLICATED on every variant. That is
/// deliberate denormalisation: it is constant within the group, and it saves the
/// card a second query to say "24,90 – 29,90 €".
/// </summary>
public sealed record ProductSearchDocument
{
    public required string Id { get; init; }              // VariantId
    public required string ProductId { get; init; }       // the collapse field
    public required string Sku { get; init; }
    public required string Culture { get; init; }         // "es" | "en"
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Brand { get; init; }

    /// <summary>The code, as a keyword: for filtering and faceting, never for
    /// matching text. Listing it among the text fields promised a match that
    /// could never happen, which is why it left them.</summary>
    public string? Category { get; init; }

    /// <summary>
    /// The whole branch in the index's culture: "Hogar Cocina Menaje de cocina".
    /// This one IS text and it is analysed. It is what lets "induction cookware"
    /// match at all, because until now the word "cookware" did not exist in the
    /// index — a code did.
    /// </summary>
    public string? CategoryPathText { get; init; }

    /// <summary>
    /// The branch's codes as keywords: `["HOME", "KITCHEN", "COOKWARE"]`.
    ///
    /// Filtering on this is what makes a category page show the whole branch.
    /// It is a keyword array and takes no part in scoring — it is not among
    /// `SearchableFields` — so adding it moves no number in the golden set, and
    /// the gate is the thing that says whether that held.
    /// </summary>
    public IReadOnlyList<string> CategoryCodes { get; init; } = [];

    /// <summary>
    /// When the source first listed it, for sorting a "new arrivals" row.
    ///
    /// Not `UpdatedAt`: a reimport touches every product in the same second, so
    /// sorting by it would put whatever was imported last at the front and call
    /// it new. Like the branch codes above it takes no part in scoring.
    /// </summary>
    public DateOnly? AvailableFrom { get; init; }

    /// <summary>Attributes flattened into searchable text: "color azul marino talla 36-42 drop 8".</summary>
    public string? AttributesText { get; init; }

    public required string Slug { get; init; }

    /// <summary>
    /// The product's public code. It travels on the document so a result card
    /// can build the product URL — <c>/p/{slug}/{code}</c> — without a second
    /// lookup. The slug alone could not: it is not a key (ADR 0026).
    /// </summary>
    public required string Code { get; init; }

    /// <summary>Axis values as "COLOR:NAVY" pairs, which is what allows
    /// filtering by an exact combination without promising one that does not exist.</summary>
    public string[] AxisValues { get; init; } = [];

    /// <summary>THIS variant's price. Exact, not a range.</summary>
    public decimal PriceAmount { get; init; }

    /// <summary>
    /// Whether THIS variant can be bought, summed across the open warehouses.
    ///
    /// Per variant and not per product, which is the same argument that made the
    /// variant the indexed unit (ADR 0015): "some variant is available" is how a
    /// card says in stock and the size you want is not. Because the document is
    /// already per variant, a filter on `inStock` is exact — and it is exact for
    /// free, with no extra field and no second query.
    ///
    /// It is a boolean and not the quantity on purpose. The number is inventory's
    /// business and it changes constantly; publishing it would mean reindexing on
    /// every single movement, and telling a shopper there are two left is a
    /// promise the shop cannot keep between the page and the checkout.
    /// </summary>
    public bool InStock { get; init; }

    public decimal PriceFrom { get; init; }
    public decimal PriceTo { get; init; }
    public required string PriceCurrency { get; init; }
    /// <summary>The image's key in the store, not a URL. The HTTP edge composes
    /// the URL, so putting a CDN in front touches neither the index nor the
    /// domain (docs/adr/0011-product-images.md).</summary>
    public string? ImageId { get; init; }
    public required string Status { get; init; }          // solo "active" es buscable

    public static string IndexNameFor(string culture) => $"products_{culture}";

    /// <summary>
    /// The attributes' searchable text, IN THE INDEX'S CULTURE.
    ///
    /// It used to be <c>$"{key} {value}"</c> over the raw data, so the English
    /// index contained "color azul marino" and no English query could match it.
    /// That is the exact cause of "navy blue shoes" and "womens running shoes"
    /// scoring 0.000 against the committed baseline.
    ///
    /// Now it comes from the definition: its label in this culture, and the
    /// option's label in this culture. With no definition it falls back to the
    /// raw code, which is the previous behaviour.
    /// </summary>
    private static string? RenderAttributes(
        Product product, string culture, AttributeDefinitions? definitions)
    {
        var parts = product.Attributes
            .Where(value => value.IsWorthIndexing)
            .Select(value =>
            {
                var definition = definitions?.ByCode(value.Code);

                // An attribute marked as not searchable does not go in: a
                // manufacturer code adds noise and nobody types it.
                if (definition is { IsSearchable: false })
                    return null;

                var label = definition?.Label.In(culture) ?? value.Code;
                var rendered = value.RenderIn(culture, definition);

                return string.IsNullOrWhiteSpace(rendered) ? label : $"{label} {rendered}";
            })
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return parts.Length == 0 ? null : string.Join(' ', parts!);
    }

    /// <summary>
    /// One document per available variant. A product with no variants should not
    /// exist — importing always mints a default one — but if one turned up it is
    /// not indexed: there is nothing to buy.
    /// </summary>
    public static IEnumerable<ProductSearchDocument> ForVariants(
        Product product, string culture,
        AttributeDefinitions? definitions = null, CategoryTree? categories = null,
        IReadOnlyDictionary<string, int>? available = null)
    {
        var (from, to) = product.PriceRange;

        return product.Variants
            .Where(variant => variant.Status == VariantStatus.Available)
            .Select(variant => FromVariant(
                product, variant, culture, from.Amount, to.Amount, definitions, categories,
                // No stock row at all is not in stock. A SKU nobody has counted
                // is a SKU nobody can ship, and defaulting the other way is how
                // a shop sells what it does not have.
                available?.GetValueOrDefault(variant.Sku, 0) > 0));
    }

    private static ProductSearchDocument FromVariant(
        Product product, Variant variant, string culture, decimal priceFrom, decimal priceTo,
        AttributeDefinitions? definitions, CategoryTree? categories, bool inStock) => new()
        {
            Id = variant.Id.ToString(),
            ProductId = product.Id.ToString(),
            Sku = variant.Sku,
            Culture = culture,
            Name = product.Name.In(culture),
            Description = product.Description?.In(culture),
            Brand = product.Brand,
            Category = product.Category,
            CategoryPathText = categories?.PathTextIn(product.Category, culture),
            CategoryCodes = categories?.BranchCodes(product.Category) ?? [],
            AvailableFrom = product.AvailableFrom,
            AttributesText = RenderAttributes(product, culture, definitions),
            Slug = product.Slug.In(culture),
            Code = product.Code,
            AxisValues = [.. variant.AxisValues.Select(pair => $"{pair.Key}:{pair.Value}")],
            PriceAmount = variant.Price.Amount,
            InStock = inStock,
            PriceFrom = priceFrom,
            PriceTo = priceTo,
            PriceCurrency = variant.Price.Currency,
            // The cover is the lowest SortOrder, and that rule lives in the
            // aggregate. Here it used to take the first of the list while the
            // backoffice listing sorted: the same product could show two different
            // photos. The variant's own photo when it has one — colour needs one,
            // size does not — and the product's cover otherwise.
            ImageId = variant.Image?.Value ?? product.PrimaryImage?.Id.Value,
            Status = product.Status.ToString().ToLowerInvariant()
        };
}
