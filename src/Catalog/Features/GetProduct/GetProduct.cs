using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.GetProduct;

/// <summary>
/// One product, in full, addressed by the slug in its URL.
///
/// It is the query behind the product detail page, and the page is the reason
/// three things were deferred: phase 1 put the variant picker off to "the cart
/// phase, where it is needed rather than decorative" and the cart phase never
/// built the page it lives on; the locale segment and <c>hreflang</c> need a URL
/// with a slug in it; and the storefront's browser spec was not written because
/// this page changes the flow it would test.
///
/// It reads the CATALOGUE and not the index, deliberately. The index carries
/// what ranking needs — rendered text, a price, a stock flag — and the page
/// needs what a shopper decides on: every attribute as structured data, every
/// image, every variant including the ones that are out of stock. It also has to
/// work when Elasticsearch does not, which is invariant 8 read the way it was
/// meant: search degrades, the shop does not go dark.
/// </summary>
public sealed record GetProductQuery(string Code, string Culture) : IQuery<ProductDetail?>;

/// <summary>
/// A rendered attribute. The VALUE is resolved here because it needs the
/// definition; the WORDING is not, because "Sí"/"Yes" is interface copy and
/// belongs in the frontend's translation files, never in an API response.
/// </summary>
public sealed record ProductAttributeView(
    string Code,
    string Label,
    string Kind,
    string? Value,
    decimal? Number,
    string? Unit,
    bool? Flag);

/// <summary>
/// One axis and every option it offers, in the catalogue's order.
///
/// The page needs the options the PRODUCT declares, not the ones its variants
/// happen to use, because a size that exists in navy and not in black has to
/// render as unavailable rather than vanish. A picker that hides what it cannot
/// sell tells the shopper their size does not exist; one that disables it tells
/// them the truth.
/// </summary>
public sealed record VariantAxisView(
    string Code, string Label, IReadOnlyList<VariantOptionView> Options);

public sealed record VariantOptionView(string Code, string Label);

public sealed record VariantView(
    string VariantId,
    string Sku,
    decimal PriceAmount,
    string PriceCurrency,
    IReadOnlyDictionary<string, string> AxisValues,
    string Label,
    string? ImageId,
    bool Discontinued,
    bool InStock,
    int? Remaining);

public sealed record ProductImageView(string ImageId, string? Alt, int SortOrder);

/// <summary>One step of the breadcrumb: the whole branch, named in the answered
/// culture.</summary>
public sealed record CategoryStep(string Code, string Name);

/// <summary>The same product's URL in another culture — what <c>hreflang</c> is
/// made of, and what a redirect from the wrong-language slug needs.</summary>
public sealed record AlternateSlug(string Culture, string Slug);

public sealed record ProductDetail(
    string ProductId,
    string Code,
    string Name,
    string Slug,
    string? Description,
    string? Brand,
    IReadOnlyList<CategoryStep> Category,
    IReadOnlyList<ProductImageView> Images,
    IReadOnlyList<ProductAttributeView> Attributes,
    IReadOnlyList<VariantAxisView> Axes,
    IReadOnlyList<VariantView> Variants,
    decimal PriceFrom,
    decimal PriceTo,
    string PriceCurrency,
    IReadOnlyList<AlternateSlug> Alternates,
    IReadOnlyList<string> MissingCultures,
    string Status,
    DateTimeOffset UpdatedAt);

public sealed class GetProductValidator : AbstractValidator<GetProductQuery>
{
    public GetProductValidator()
    {
        // Checked before it reaches the database, so a mistyped URL is a 404
        // and not a query. It is also the only place that knows a code is ten
        // characters of a particular alphabet.
        RuleFor(query => query.Code)
            .Must(ProductCode.IsWellFormed)
            .WithMessage($"A product code is {ProductCode.Length} characters of Crockford base32.");

        RuleFor(query => query.Culture).Must(culture => culture is "es" or "en")
            .WithMessage("Supported cultures: es, en.");
    }
}

public sealed class GetProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/catalog/products/{code}?culture=es
        //
        // The API takes the CODE alone and knows nothing about the slug in the
        // storefront's URL (ADR 0026). That is the whole benefit of the split:
        // the page is /p/{slug}/{code}, the slug is there for humans and for
        // crawlers, and this endpoint never has to decide what a slug means.
        //
        // The response carries the canonical slug, so the storefront compares it
        // against the one in its own address bar and redirects when they differ
        // — which makes a rename a 301 rather than a 404 without anything here
        // keeping a history of names.
        app.MapGet("/api/catalog/products/{code}",
            async Task<Results<Ok<ProductDetail>, NotFound>> (
                   string code, string? culture,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(
                    culture, http.Request.Headers.AcceptLanguage);

                // Declared before the result is known, so a 404 says which
                // language it looked in. A miss with no Content-Language is a
                // miss nobody can reproduce.
                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                var product = await dispatcher.SendAsync(
                    // Folded up and de-ambiguated here: people lowercase URLs,
                    // and answering 404 to a correct code in the wrong case
                    // loses a visitor for nothing.
                    new GetProductQuery(ProductCode.Normalise(code), resolved), ct);

                return product is null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(product);
            })
            .AllowAnonymous()   // a product page is public, and this says so in code
            .WithTags("Catalog")
            .WithName("GetProduct");
    }
}

public sealed class GetProductHandler(
    IProductCatalogReader products,
    IAttributeDefinitionReader definitions,
    ICategoryReader categories,
    IAvailabilityReader availability)
    : IQueryHandler<GetProductQuery, ProductDetail?>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);
    private static readonly string[] Cultures = ["es", "en"];

    /// <summary>
    /// Below this, the exact count is disclosed; above it, only that there is
    /// stock. The number is only worth showing when it is a reason to hurry, and
    /// a shop that publishes its shelf depth publishes it to its competitors too.
    /// </summary>
    private const int LowStock = 5;

    public async Task<ProductDetail?> HandleAsync(
        GetProductQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.get");
        activity?.SetTag("catalog.code", query.Code);
        activity?.SetTag("catalog.culture", query.Culture);

        var product = await products.FindByCodeAsync(query.Code, cancellationToken);

        if (product is null || product.Status != ProductStatus.Active)
        {
            // A Draft product is not a 403, it is a 404: it does not exist as far
            // as the storefront is concerned, and saying "exists but not for you"
            // leaks the catalogue that is still under review.
            activity?.SetTag("catalog.found", false);
            return null;
        }

        activity?.SetTag("catalog.found", true);
        activity?.SetTag("catalog.product_id", product.Id.Value);

        var attributes = await definitions.AllAsync(cancellationToken);
        var tree = await categories.AllAsync(cancellationToken);

        // One call for every SKU on the page rather than one per variant: the
        // port takes a collection precisely so a product with eight variants is
        // one query and not eight.
        var skus = product.Variants.Select(variant => variant.Sku).ToArray();
        var available = skus.Length == 0
            ? new Dictionary<string, int>()
            : await availability.AvailableAsync(skus, cancellationToken);

        var (from, to) = product.PriceRange;

        return new ProductDetail(
            product.Id.Value.ToString(),
            product.Code,
            product.Name.In(query.Culture),
            product.Slug.In(query.Culture),
            product.Description?.In(query.Culture),
            product.Brand,
            Breadcrumb(tree, product.Category, query.Culture),
            [.. product.Images
                .OrderBy(image => image.SortOrder)
                .Select(image => new ProductImageView(
                    image.Id.Value, image.Alt?.In(query.Culture), image.SortOrder))],
            [.. product.Attributes
                .Select(value => Render(value, attributes.ByCode(value.Code), query.Culture))
                .OrderBy(view => view.Label, StringComparer.OrdinalIgnoreCase)],
            [.. product.VariantAxes.Select(axis => Axis(axis, attributes.ByCode(axis), query.Culture))],
            [.. product.Variants.Select(variant =>
                View(variant, product, attributes, available, query.Culture))],
            from.Amount,
            to.Amount,
            from.Currency,
            // Every culture the product has a slug in, itself included: a page
            // that lists its own canonical URL beside its alternates is what
            // hreflang actually requires, and leaving itself out is the mistake
            // every hand-written implementation makes.
            [.. product.Slug.Values
                .Select(pair => new AlternateSlug(pair.Key, pair.Value))
                .OrderBy(alternate => alternate.Culture, StringComparer.Ordinal)],
            [.. Cultures.Where(culture => !product.Name.Cultures.Contains(culture))],
            product.Status.ToString().ToLowerInvariant(),
            product.UpdatedAt);
    }

    private static IReadOnlyList<CategoryStep> Breadcrumb(
        CategoryTree tree, string? code, string culture)
    {
        var category = tree.ByCode(code);
        if (category is null)
            return [];

        // The whole branch and not the leaf, for the same reason the index holds
        // the whole branch: "Hogar > Cocina > Menaje de cocina" is where the
        // shopper is, and the leaf alone does not say it.
        return
        [
            .. category.Ancestry
                .Select(tree.ByCode)
                .Where(ancestor => ancestor is not null)
                .Select(ancestor => new CategoryStep(ancestor!.Code, ancestor.Name.In(culture)))
        ];
    }

    private static ProductAttributeView Render(
        AttributeValue value, AttributeDefinition? definition, string culture) =>
        new(value.Code,
            // A value whose definition was never created still shows its code
            // rather than nothing: an unlabelled row is a gap somebody can see
            // and fix, an absent one is a gap nobody knows about.
            definition?.Label.In(culture) ?? value.Code,
            value.Kind.ToString().ToLowerInvariant(),
            value.Kind is AttributeKind.Number or AttributeKind.Boolean
                ? null
                : value.RenderIn(culture, definition),
            value.Number,
            definition?.Unit,
            value.Flag);

    private static VariantAxisView Axis(string code, AttributeDefinition? definition, string culture) =>
        new(code,
            definition?.Label.In(culture) ?? code,
            definition is null
                ? []
                : [.. definition.Options.Select(option =>
                    new VariantOptionView(option.Code, option.Label.In(culture)))]);

    private static VariantView View(
        Variant variant,
        Product product,
        AttributeDefinitions definitions,
        IReadOnlyDictionary<string, int> available,
        string culture)
    {
        var remaining = available.GetValueOrDefault(variant.Sku);

        return new VariantView(
            variant.Id.Value.ToString(),
            variant.Sku,
            variant.Price.Amount,
            variant.Price.Currency,
            variant.AxisValues,
            // "Azul marino / 38", not "NAVY_BLUE / 38". The domain's LabelFor
            // joins the CODES, because that is what gets frozen onto an order
            // line and a frozen label must not depend on a translation somebody
            // can edit afterwards. What a page shows is the other thing.
            Label(variant, product, definitions, culture),
            // A variant with no photo of its own borrows the product's: size does
            // not change what a thing looks like, colour does, and only the ones
            // that differ carry an image.
            (variant.Image ?? product.PrimaryImage?.Id)?.Value,
            variant.Status == VariantStatus.Discontinued,
            remaining > 0,
            remaining is > 0 and <= LowStock ? remaining : null);
    }

    private static string Label(
        Variant variant, Product product, AttributeDefinitions definitions, string culture) =>
        string.Join(" · ", product.VariantAxes
            .Where(variant.AxisValues.ContainsKey)
            .Select(axis => definitions.ByCode(axis)?.LabelForOption(variant.AxisValues[axis], culture)
                            ?? variant.AxisValues[axis]));
}
