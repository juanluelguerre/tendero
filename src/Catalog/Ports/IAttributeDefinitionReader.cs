using ElGuerre.Tendero.Catalog.Domain;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// Todas las definiciones de atributo, de una vez.
///
/// Es un puerto y no una consulta suelta porque lo necesitan TRES sitios: el
/// mapeador de importación (para resolver "azul marino" a NAVY_BLUE), la
/// proyección al índice (para renderizar "navy blue" en el índice inglés) y el
/// backoffice. Con tres consumidores, CLAUDE.md dice que es un puerto.
///
/// Devuelve el catálogo entero y no una definición cada vez a propósito: son
/// decenas de filas que cambian con muy poca frecuencia, y una proyección que
/// consultase por atributo haría N+1 en cada reindexado.
/// </summary>
public interface IAttributeDefinitionReader
{
    Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Las definiciones, indexadas por código y resolubles por alias. Es una clase
/// y no un diccionario porque la resolución por alias es lógica, y repartirla
/// por cada consumidor es como se acaba con tres reglas distintas.
/// </summary>
public sealed class AttributeDefinitions(IReadOnlyList<AttributeDefinition> definitions)
{
    public static readonly AttributeDefinitions Empty = new([]);

    private readonly Dictionary<string, AttributeDefinition> _byCode =
        definitions.ToDictionary(definition => definition.Code, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AttributeDefinition> All { get; } = definitions;

    public AttributeDefinition? ByCode(string code) =>
        _byCode.GetValueOrDefault(AttributeDefinition.Normalise(code));

    /// <summary>La definición que responde a la clave que manda un origen.</summary>
    public AttributeDefinition? ForSourceKey(string key) =>
        ByCode(key) ?? All.FirstOrDefault(definition => definition.AnswersTo(key));
}
