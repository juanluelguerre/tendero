using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Search.Features.ReindexProducts;

/// <summary>
/// Rehace el índice desde Postgres. docs/architecture.md ya dice que el índice
/// es una proyección desechable y que se reindexa a voluntad; lo que faltaba era
/// el "a voluntad".
///
/// Hace falta de verdad, no en teoría: Elasticsearch se declara con
/// ContainerLifetime.Persistent pero SIN volumen, así que sus documentos viven
/// en la capa de escritura del contenedor. En cuanto Aspire lo recrea, el índice
/// queda vacío con el catálogo entero en Active — y nada lo repone, porque el
/// outbox ya entregó esos eventos y volver a publicar un producto ya activo no
/// cambia nada (por diseño).
///
/// Aplica la MISMA regla que la proyección del outbox, y literalmente el mismo
/// código: <see cref="ProductIndexProjection"/>. Active se indexa, lo demás se
/// retira, así que el reindexado CONVERGE — un producto que dejó de estar activo
/// sale del índice — en lugar de limitarse a añadir.
///
/// No recrea los índices ni toca sus mappings: eso es responsabilidad de
/// SearchIndexInitializer, y duplicar aquí la definición del mapping seria tener
/// dos fuentes para la misma verdad.
///
/// LÍMITE conocido: converge para los productos que EXISTEN. Un producto
/// borrado de la tabla deja su documento huérfano, porque el recorrido no puede
/// ver lo que ya no está. No ocurre por la aplicación —el dominio no borra,
/// archiva, y Archive() emite ProductArchived que lo retira— pero sí con un
/// DELETE a mano o restaurando una copia antigua de la base. Visto de verdad:
/// seis filas borradas por SQL dejaron doce documentos para seis productos.
/// Cerrarlo pide borrar los índices antes de reconstruir, y eso mueve la
/// propiedad del mapping; se decide cuando exista un caso que no sea un reset
/// manual en desarrollo.
/// </summary>
public sealed record ReindexProductsCommand : ICommand<ReindexProductsResult>;

public sealed record ReindexProductsResult(int Indexed, int Removed, double ElapsedSeconds);

public sealed class ReindexProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/search/reindex
        app.MapPost("/api/search/reindex",
            async Task<Ok<ReindexProductsResult>> (ICommandDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ReindexProductsCommand(), ct)))
            .WithTags("Search")
            .WithName("ReindexProducts");
    }
}

public sealed class ReindexProductsHandler(
    IProductReader products,
    IProductIndexer indexer)
    : ICommandHandler<ReindexProductsCommand, ReindexProductsResult>
{
    private static readonly ActivitySource Telemetry = new("ElGuerre.Tendero.Search");

    public async Task<ReindexProductsResult> HandleAsync(
        ReindexProductsCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("search.reindex");
        var stopwatch = Stopwatch.StartNew();

        int indexed = 0, removed = 0;

        // Stream, no ToList: con el catálogo completo de Amazon Berkeley Objects
        // (147k) cargarlo entero en memoria para recorrerlo una vez no tiene
        // sentido, y el lector ya lo entrega perezosamente.
        await foreach (var product in products.StreamAllAsync(cancellationToken))
        {
            if (await ProductIndexProjection.ApplyAsync(indexer, product, cancellationToken))
                indexed++;
            else
                removed++;
        }

        stopwatch.Stop();
        activity?.SetTag("search.reindex.indexed", indexed);
        activity?.SetTag("search.reindex.removed", removed);

        return new ReindexProductsResult(indexed, removed, stopwatch.Elapsed.TotalSeconds);
    }
}
