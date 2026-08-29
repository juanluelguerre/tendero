namespace Tendero.SearchEval;

/// <summary>
/// Borra los índices antes de evaluar. Es imprescindible para que la puerta sea
/// reproducible: cada ejecución construye los agregados en memoria y sus
/// ProductId son GUID v7 nuevos, así que sin borrar, los documentos de la pasada
/// anterior se quedan y compiten en el ranking con los de esta. Dos ejecuciones
/// del mismo código daban 0.674 y 0.360.
///
/// Va por HTTP plano y no por un puerto porque hoy no existe ninguno: el
/// "índice es una proyección desechable" (docs/architecture.md) todavía no tiene
/// operación de reconstrucción. Cuando el reindexado sea una funcionalidad y no
/// una necesidad de esta herramienta, será un puerto y un ADR.
/// </summary>
public static class IndexReset
{
    public static async Task DropAsync(
        string elasticsearch, IEnumerable<string> indexNames, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = new Uri(elasticsearch) };

        foreach (var index in indexNames)
        {
            // 404 es la respuesta normal la primera vez; sólo interesa que no
            // quede nada de una ejecución anterior.
            using var response = await client.DeleteAsync(new Uri(index, UriKind.Relative), cancellationToken);

            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException(
                    $"Could not drop index '{index}': {response.StatusCode}.");
        }
    }
}
