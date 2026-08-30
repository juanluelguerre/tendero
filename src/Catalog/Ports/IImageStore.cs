using Tendero.SharedKernel;

namespace Tendero.Catalog.Ports;

/// <summary>
/// Almacén de imágenes de producto. Puerto, no implementación: hoy hay un
/// adaptador de sistema de ficheros; el día que haga falta escalar entra uno de
/// S3 (SeaweedFS en local, R2/S3/B2 en producción) sin tocar el dominio, igual
/// que con los conectores de catálogo y los proveedores de pago.
///
/// El contenido se direcciona por su hash: guardar dos veces la misma imagen
/// devuelve la misma <see cref="ImageId"/> y escribe una sola vez.
/// </summary>
public interface IImageStore
{
    Task<ImageId> SaveAsync(Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Devuelve null si la clave no existe. Un id inexistente es un 404,
    /// no una excepción: las claves vienen de la URL y el usuario puede inventarlas.</summary>
    Task<StoredImage?> OpenAsync(ImageId id, CancellationToken ct = default);
}

public sealed record StoredImage(Stream Content, string ContentType);
