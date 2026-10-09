using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Ports;

/// <summary>
/// The attributes a claim may be typed with, as Catalog defines them — handed
/// over as values by whoever composes the two (ADR 0014): Knowledge never reads
/// an <c>AttributeDefinition</c>.
///
/// **By what the claim is read from** (phase 13a, I-4). An extractor fits what it
/// reads into the vocabulary it is given: handed all of it with a picture's
/// reading, it typed a strap's grey as a lamp's colour temperature and a plastic
/// buckle as the product's material. So a reading is offered only what a picture
/// can show, and what it types beyond that is refused by the vetting, which
/// checks against the same list.
///
/// **And by the product** (ADR 0056). The same reasoning one step further: a
/// backpack offered a coffee maker's cups has a wrong drawer within reach. What
/// a product is offered is what its category's branch declares.
/// </summary>
public interface IAttributeVocabulary
{
    /// <param name="product">The product the claims are about.</param>
    /// <param name="readFrom">The kind of source the claims will be read from.</param>
    Task<IReadOnlyList<AttributeSpec>> CurrentAsync(ProductId product, SourceKind readFrom, CancellationToken cancellationToken = default);
}
