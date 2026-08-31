using Tendero.Catalog.Domain;
using Tendero.SharedKernel;

namespace Tendero.Catalog.Ports;

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

/// <summary>
/// Confirma la unidad de trabajo. Los eventos de dominio pendientes viajan a la
/// tabla outbox en ESTA misma transacción (invariante 7): quien no llama aquí,
/// no ha publicado nada.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
