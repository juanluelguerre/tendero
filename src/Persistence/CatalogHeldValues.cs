using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Catalog's attribute values as Knowledge's review queue takes them (ADR 0014),
/// beside <see cref="CatalogProductFacts"/> for the same reason: Persistence
/// already knows both contexts, and both hosts need it.
///
/// Every product the catalogue still has, whatever its status: a claim is
/// reviewed while its product is a draft. An attribute the catalogue no longer
/// defines is held all the same — it is still what the product says — and is
/// shown as it was stored.
/// </summary>
internal sealed class CatalogHeldValues(
    IProductRepository products,
    IAttributeDefinitionReader definitions) : ICatalogueValues
{
    public async Task<IReadOnlyDictionary<ProductId, IReadOnlyList<HeldValue>>> OfAsync(
        IReadOnlyCollection<ProductId> productIds, string culture, CancellationToken cancellationToken = default)
    {
        var attributes = await definitions.AllAsync(cancellationToken);
        var held = new Dictionary<ProductId, IReadOnlyList<HeldValue>>();

        // One lookup per product: a page of the queue names a handful.
        foreach (var id in productIds.Distinct())
        {
            if (await products.FindByIdAsync(id, cancellationToken) is not { } product)
                continue;

            held[id] =
            [
                .. product.Attributes.Select(value =>
                    new HeldValue(value.Code, value.Key, value.RenderIn(culture, attributes.ByCode(value.Code)))),
            ];
        }

        return held;
    }
}
