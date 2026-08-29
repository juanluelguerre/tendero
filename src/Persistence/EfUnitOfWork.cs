using Tendero.Catalog.Features.ImportProducts;

namespace Tendero.Persistence;

/// <summary>
/// Confirmar la unidad de trabajo pasa por TenderoDbContext.SaveChangesAsync,
/// que es donde los eventos de dominio se vuelcan al outbox en la misma
/// transacción. Por eso el puerto no expone nada más: no hay forma de guardar
/// sin publicar lo que el agregado levantó.
/// </summary>
internal sealed class EfUnitOfWork(TenderoDbContext context) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}
