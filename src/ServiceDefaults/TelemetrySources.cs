namespace Tendero.ServiceDefaults;

/// <summary>
/// Los ActivitySource de cada slice con E/S, en un solo sitio. Añadir un slice
/// con E/S significa añadir aquí su nombre — si no aparece, no se traza, y eso
/// se ve en el dashboard antes que en producción.
/// </summary>
public static class TelemetrySources
{
    public const string Catalog = "Tendero.Catalog";
    public const string Search = "Tendero.Search";
    public const string Outbox = "Tendero.Outbox";

    public static readonly string[] All = [Catalog, Search, Outbox];
}
