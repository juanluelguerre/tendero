using ElGuerre.Tendero.Catalog.Domain;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// Every attribute definition, at once.
///
/// A port and not a loose query because THREE places need it: the import mapper
/// (to resolve "azul marino" to NAVY_BLUE), the index projection (to render
/// "navy blue" into the English index) and the backoffice. With three consumers,
/// CLAUDE.md says it is a port.
///
/// It returns the whole catalogue rather than one definition at a time, on
/// purpose: these are dozens of rows that change very rarely, and a projection
/// querying per attribute would be N+1 on every reindex.
/// </summary>
public interface IAttributeDefinitionReader
{
    Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The definitions, indexed by code and resolvable by alias. A class and not a
/// dictionary because alias resolution is logic, and spreading it across each
/// consumer is how you end up with three different rules.
/// </summary>
public sealed class AttributeDefinitions(IReadOnlyList<AttributeDefinition> definitions)
{
    public static readonly AttributeDefinitions Empty = new([]);

    private readonly Dictionary<string, AttributeDefinition> byCode =
        definitions.ToDictionary(definition => definition.Code, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AttributeDefinition> All { get; } = definitions;

    public AttributeDefinition? ByCode(string code) =>
        this.byCode.GetValueOrDefault(AttributeDefinition.Normalise(code));

    /// <summary>The definition that answers to the key a source sends.</summary>
    public AttributeDefinition? ForSourceKey(string key) =>
        ByCode(key) ?? All.FirstOrDefault(definition => definition.AnswersTo(key));
}
