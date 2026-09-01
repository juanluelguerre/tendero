using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// Acceso al agregado Product. Vivía dentro del slice ImportProducts mientras
/// fue el único que lo usaba; con PublishProduct pasa a ser compartido, y la
/// regla dice que lo compartido sale a puertos, nunca a una referencia entre
/// slices (CLAUDE.md, invariante 2). El test de arquitectura lo comprueba.
/// </summary>
public interface IProductRepository
{
    Task<Product?> FindByIdAsync(ProductId id, CancellationToken ct);
    Task<Product?> FindByExternalReferenceAsync(string source, string externalId, CancellationToken ct);
    void Add(Product product);
}

/// <summary>Una página de productos con el total de la consulta completa, no el
/// de la página: una cola de revisión necesita saber cuántos quedan.</summary>
public sealed record ProductPage(IReadOnlyList<Product> Items, int Total);

/// <summary>
/// Lado de lectura del catálogo. Separado de <see cref="IProductRepository"/>
/// porque son responsabilidades distintas: aquél carga agregados para mutarlos,
/// éste proyecta listados para pintarlos. Mezclarlas acaba en un repositorio con
/// veinte métodos del que nadie sabe qué mitad usa cada slice.
/// </summary>
public interface IProductCatalogReader
{
    Task<ProductPage> ListAsync(ProductStatus? status, int page, int pageSize, CancellationToken ct);
}

/// <summary>
/// Confirma la unidad de trabajo. Los eventos de dominio pendientes viajan a la
/// tabla outbox en ESTA misma transacción (invariante 7): quien no llama aquí,
/// no ha publicado nada.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
