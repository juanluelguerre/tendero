using ElGuerre.Tendero.Knowledge.Extraction;

namespace ElGuerre.Tendero.Knowledge.Ports;

/// <summary>
/// The attributes a claim may be typed with, as Catalog defines them — handed
/// over as values by whoever composes the two (ADR 0014): Knowledge never reads
/// an <c>AttributeDefinition</c>.
/// </summary>
public interface IAttributeVocabulary
{
    Task<IReadOnlyList<AttributeSpec>> CurrentAsync(CancellationToken cancellationToken = default);
}
