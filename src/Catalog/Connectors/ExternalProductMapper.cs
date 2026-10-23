using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Connectors;

/// <summary>
/// Turns what a connector delivers into the catalogue aggregate. It exists as a
/// piece of its own because it has TWO consumers and neither can call the other:
/// the <c>ImportProducts</c> slice and the quality gate <c>tools/SearchEval</c>,
/// which indexes the seed catalogue without going through Postgres.
///
/// While it was written twice, SearchEval's comment read "same steps and same
/// order as ImportProductsHandler" — which is the polite way of saying the gate
/// would be measuring a different system the moment importing changed.
///
/// It lives in <c>Connectors</c> and not in <c>Domain</c> on purpose: the domain
/// may only see the SharedKernel (invariant 4), so it is the inbound contract
/// that knows the aggregate, never the other way round. The architecture test
/// checks it.
///
/// Images do NOT enter here: ingesting them is I/O against the store, and the
/// slice is what does it. The evaluation does not need them because they are not
/// a searchable field.
/// </summary>
public static class ExternalProductMapper
{
    /// <summary>Creation: a new product in Draft, already linked to its source.</summary>
    public static Product ToNewProduct(
        this ExternalProduct external, string source, TimeProvider clock,
        AttributeDefinitions? definitions = null)
    {
        var product = Product.Create(
            clock,
            external.LocalizedName,
            external.Price,
            external.LocalizedDescription,
            external.Brand,
            external.Category);

        product.SetAvailableFrom(clock, external.AvailableFrom);
        product.LinkExternal(clock, source, external.ExternalId, external.Handle);
        CopyAttributes(external, product, clock, definitions);
        EnsureVariants(external, product, clock, definitions, source);

        return product;
    }

    /// <summary>
    /// Re-import: the source rules over what the source owns. It does not touch
    /// status — an already published product does not go back to Draft because
    /// its supplier changed a description (ADR 0012).
    /// </summary>
    /// <param name="source">The source it comes from again, when the caller knows
    /// it: its handle then follows, and its variants are found by the source's own
    /// ids. Without it a re-import finds variants by SKU alone, as it always did.</param>
    public static void ApplyTo(
        this ExternalProduct external, Product product, TimeProvider clock,
        AttributeDefinitions? definitions = null, string? source = null)
    {
        product.UpdateDetails(
            clock, external.LocalizedName, external.LocalizedDescription, external.Brand, external.Category);
        product.SetPrice(clock, external.Price);
        product.SetAvailableFrom(clock, external.AvailableFrom);
        if (source is not null)
            product.LinkExternal(clock, source, external.ExternalId, external.Handle);
        CopyAttributes(external, product, clock, definitions);
        EnsureVariants(external, product, clock, definitions, source);
    }

    /// <summary>The SKU a source's variant has here: the product's external id and what the source tells it apart by.</summary>
    public static string SkuOf(ExternalProduct external, ExternalVariant variant) => $"{external.ExternalId}-{variant.Key}";

    /// <summary>
    /// The SKU a new variant is given: the source's own when it has one and no
    /// sibling shares it — a merchant keeps the labels on its shelves (phase 16,
    /// spec decision 1) — and ours otherwise. A shared SKU would make two
    /// variants one in every context that speaks SKUs.
    /// </summary>
    private static string NewSkuOf(ExternalProduct external, ExternalVariant variant)
    {
        if (string.IsNullOrWhiteSpace(variant.Sku))
            return SkuOf(external, variant);

        var sku = variant.Sku.Trim();
        var shared = external.Variants!.Count(sibling =>
            string.Equals(sibling.Sku?.Trim(), sku, StringComparison.OrdinalIgnoreCase)) > 1;

        return shared ? SkuOf(external, variant) : sku;
    }

    /// <summary>
    /// The source's words for what a variant varies by, or for what a picture
    /// shows, as the catalogue says them: the attribute's code and, for an
    /// option, the option's. One place, so a variant and the picture of it land
    /// on the same value.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Coordinates(
        IReadOnlyDictionary<string, string> words, AttributeDefinitions? definitions) =>
        words
            .Select(word => Resolve(word.Key, word.Value, definitions))
            .ToDictionary(value => value.Code, value => value.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The ways the product is bought. A source that tells none apart gets the
    /// default variant; one that does gets each of them, placed on the axes it
    /// varies by — the attribute codes its own words resolve to, in the order
    /// the source first names them.
    ///
    /// The axes are declared before the variants, as the aggregate asks, and
    /// declaring them retires a default variant an earlier import left: the
    /// product stops having exactly one way of being bought the moment the
    /// source says it has two. Re-importing adds nothing: a variant is found by
    /// the source's id for it when there is one, by its SKU otherwise, and only
    /// its price, barcode and tax class follow the source. Its SKU never does (phase 16).
    /// </summary>
    private static void EnsureVariants(
        ExternalProduct external, Product product, TimeProvider clock, AttributeDefinitions? definitions,
        string? source)
    {
        if (external.Variants is not { Count: > 0 } declared)
        {
            EnsureDefaultVariant(external, product, clock);
            return;
        }

        var placed = declared
            .Select(variant => (
                Source: variant,
                Existing: Existing(product, source, external, variant),
                Coordinates: Coordinates(variant.Attributes, definitions)))
            .ToList();

        string[] axes = [.. placed.SelectMany(variant => variant.Coordinates.Keys).Distinct(StringComparer.OrdinalIgnoreCase)];
        // Declared again, unchanged, when the product still holds a value of its
        // own on an axis — a catalogue imported before an axis was the variants'
        // alone: declaring the axes is what takes that value off.
        if (!axes.SequenceEqual(product.VariantAxes, StringComparer.OrdinalIgnoreCase)
            || axes.Any(axis => product.AttributeFor(axis) is not null))
            product.DefineAxes(clock, axes);

        foreach (var (variant, existing, coordinates) in placed)
        {
            var price = variant.PriceAmount is { } amount ? new Money(amount, external.PriceCurrency) : external.Price;
            var sku = existing?.Sku ?? product.AddVariant(clock, NewSkuOf(external, variant), price, coordinates).Sku;

            product.SetVariantPrice(clock, sku, price);
            product.SetVariantBarcode(clock, sku, variant.Barcode);
            // A source that says nothing of tax leaves the variant's class as it is.
            if (variant.TaxClass is { Length: > 0 } taxClass)
                product.SetVariantTaxClass(clock, sku, taxClass);
            if (source is not null && variant.ExternalId is { } id)
                product.LinkVariantExternal(clock, sku, source, id);
        }
    }

    private static Variant? Existing(Product product, string? source, ExternalProduct external, ExternalVariant variant) =>
        (source is not null && variant.ExternalId is { } id ? product.VariantFromSource(source, id) : null)
        ?? product.VariantBySku(NewSkuOf(external, variant))
        ?? product.VariantBySku(SkuOf(external, variant));

    /// <summary>
    /// Everything purchasable is a variant, including what arrives with none.
    ///
    /// A source that draws no distinction between sizes or colours is describing
    /// a product with a single way of being bought, and calling that a "default
    /// variant" is more honest than leaving the cart and the order line with two
    /// paths — one with a variant and one without — to be maintained in parallel
    /// forever.
    ///
    /// The SKU is derived from the external id, so re-importing does not create a
    /// second one: <c>AddVariant</c> is idempotent by SKU.
    /// </summary>
    private static void EnsureDefaultVariant(ExternalProduct external, Product product, TimeProvider clock)
    {
        if (product.Variants.Count > 0)
        {
            // Re-import: the source rules over the price, as over the rest of
            // the product.
            product.SetVariantPrice(clock, DefaultSku(external), external.Price);
            return;
        }

        product.AddVariant(clock, DefaultSku(external), external.Price);
    }

    private static string DefaultSku(ExternalProduct external) => $"{external.ExternalId}-DEFAULT";

    /// <summary>
    /// Translates what the source sends into typed values, resolving against the
    /// catalogue's definitions. This is where "azul marino" stops being the data
    /// and becomes the option <c>NAVY_BLUE</c>, which knows how to say itself in
    /// English.
    ///
    /// With no definitions — SearchEval before seeding them, a test — it falls
    /// back to plain text, which is exactly the previous behaviour. Degrading to
    /// what was already there beats failing: importing should not depend on
    /// somebody having defined the attributes first.
    /// </summary>
    private static void CopyAttributes(
        ExternalProduct external, Product product, TimeProvider clock, AttributeDefinitions? definitions)
    {
        foreach (var (name, value) in external.Attributes)
        {
            // A value the source says for the product on an axis it varies by is
            // its variants' to say (the aggregate refuses it).
            var resolved = Resolve(name, value, definitions);
            if (!product.VariesBy(resolved.Code))
                product.SetAttribute(clock, resolved);
        }
    }

    internal static AttributeValue Resolve(string name, string value, AttributeDefinitions? definitions)
    {
        var definition = definitions?.ForSourceKey(name);
        if (definition is null)
            return AttributeValue.Plain(name, value);

        // What the definition does not accept is kept as it came rather than
        // discarded: losing the data would be worse, and this way it stays visible
        // to whoever reviews. A yes/no that is neither reads as no, as it always did.
        return definition.Accept(value)
               ?? (definition.Kind == AttributeKind.Boolean
                   ? AttributeValue.Boolean(definition.Code, false)
                   : AttributeValue.Plain(definition.Code, value));
    }
}
