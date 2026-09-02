using ElGuerre.Tendero.Catalog.Domain;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// El árbol de categorías, entero. Lo necesitan la proyección al índice (para
/// renderizar la rama en cada cultura) y el backoffice; con dos consumidores y
/// uno más a la vista, es un puerto y no una consulta suelta.
/// </summary>
public interface ICategoryReader
{
    Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default);
}
