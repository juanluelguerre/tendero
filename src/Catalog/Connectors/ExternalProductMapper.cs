using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;

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

        product.LinkExternal(clock, source, external.ExternalId);
        CopyAttributes(external, product, clock, definitions);
        EnsureDefaultVariant(external, product, clock);

        return product;
    }

    /// <summary>
    /// Re-import: the source rules over what the source owns. It does not touch
    /// status — an already published product does not go back to Draft because
    /// its supplier changed a description (ADR 0012).
    /// </summary>
    public static void ApplyTo(
        this ExternalProduct external, Product product, TimeProvider clock,
        AttributeDefinitions? definitions = null)
    {
        product.UpdateDetails(
            clock, external.LocalizedName, external.LocalizedDescription, external.Brand, external.Category);
        product.SetPrice(clock, external.Price);
        CopyAttributes(external, product, clock, definitions);
        EnsureDefaultVariant(external, product, clock);
    }

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
            product.SetAttribute(clock, Resolve(name, value, definitions));
    }

    internal static AttributeValue Resolve(string name, string value, AttributeDefinitions? definitions)
    {
        var definition = definitions?.ForSourceKey(name);
        if (definition is null)
            return AttributeValue.Plain(name, value);

        return definition.Kind switch
        {
            AttributeKind.Option => definition.ResolveOption(value) is { } option
                ? AttributeValue.Option(definition.Code, option.Code)
                // The source sent a value the definition does not know. It is
                // kept as-is rather than discarded: losing the data would be
                // worse, and this way it stays visible to whoever reviews.
                : AttributeValue.Plain(definition.Code, value),

            AttributeKind.Number => decimal.TryParse(
                value.TrimEnd('"'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var number)
                    ? AttributeValue.Numeric(definition.Code, number)
                    : AttributeValue.Plain(definition.Code, value),

            AttributeKind.Boolean => AttributeValue.Boolean(
                definition.Code,
                value.Trim() is "si" or "sí" or "yes" or "true" or "1"),

            _ => AttributeValue.Plain(definition.Code, value)
        };
    }
}
