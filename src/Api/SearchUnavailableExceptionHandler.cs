using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Tendero.Search.Contracts;

namespace Tendero.Api;

/// <summary>
/// Traduce un fallo del motor de búsqueda a 503. Dos cosas que hace a propósito:
///
/// El estado es 503 y no 500. El servicio está bien, su dependencia no: eso es
/// reintentable y un cliente (o un agente) debe poder distinguirlo.
///
/// El cuerpo NO lleva el detalle. El cliente de Elastic devuelve un audit trail
/// con la traza completa, el nodo y el puerto interno; sirve para diagnosticar y
/// por eso viaja en la excepción, que ASP.NET registra entera en el log. Lo que
/// nunca debe hacer es salir por la respuesta: expone topología y no le dice
/// nada útil a quien buscaba una cafetera. El texto sigue la voz de DESIGN.md
/// —qué ha pasado y qué hacer—, no una disculpa.
/// </summary>
internal sealed class SearchUnavailableExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not SearchUnavailableException)
            return false;

        var problem = TypedResults.Problem(
            title: "Search is unavailable right now.",
            detail: "The catalogue is fine; the search engine is not answering. Try again in a moment.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

        await problem.ExecuteAsync(httpContext);

        return true;
    }
}
