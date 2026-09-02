using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Catalog.Adapters;

/// <summary>
/// Las definiciones que trae el repositorio, cacheadas para el proceso.
///
/// Es el adaptador por defecto y el que usa la puerta de calidad, que
/// deliberadamente no toca Postgres. Cachea porque la proyección al índice las
/// pide una vez por producto y por cultura: sin caché, reindexar seis productos
/// leería el fichero veinticuatro veces.
///
/// Cuando el backoffice permita editarlas (fase 2, pantalla pendiente), el
/// adaptador que lea de Postgres se registra en su lugar y nada más cambia —
/// que es la razón de que esto sea un puerto.
/// </summary>
internal sealed class SeedFileAttributeDefinitionReader(
    IOptions<AttributeSeedOptions> options, TimeProvider clock) : IAttributeDefinitionReader
{
    private AttributeDefinitions? _cached;

    public async Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
            return _cached;

        var path = options.Value.FilePath;

        // Sin fichero se sigue: un catálogo sin definiciones indexa los
        // atributos como texto plano, que es el comportamiento anterior. Fallar
        // aquí convertiría un dato que falta en una tienda caída.
        if (!File.Exists(path))
            return _cached = AttributeDefinitions.Empty;

        return _cached = new AttributeDefinitions(
            await AttributeDefinitionSeedFile.LoadAsync(path, clock, cancellationToken));
    }
}
