namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// The SKU this shop sells each of a source's variants under (phase 16, S2a).
/// A source counts its stock by its own variants, and the shop by SKU — which is
/// the source's own SKU only when it had one no sibling shared (spec decision 1).
/// So whoever brings a source's stock asks the catalogue, by the source's ids,
/// and Inventory never has to know the catalogue at all.
/// </summary>
public interface ISourceVariantSkus
{
    /// <returns>The source's variant id → our SKU; a variant the catalogue has
    /// not imported from that source is absent.</returns>
    Task<IReadOnlyDictionary<string, string>> SkusOfAsync(
        string source, IReadOnlyCollection<string> externalVariantIds, CancellationToken ct);
}
