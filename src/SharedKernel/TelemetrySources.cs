namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// Los nombres de <c>ActivitySource</c> y <c>Meter</c> del sistema, en un solo
/// sitio. Añadir un slice con E/S significa añadir aquí su nombre — si no
/// aparece en <see cref="All"/>, OpenTelemetry no lo registra y el slice deja de
/// trazarse sin que nada falle.
///
/// Viven en el SharedKernel y no en ServiceDefaults porque hacen falta en los dos
/// extremos: quien ABRE el span (un slice de Catalog o de Search) y quien lo
/// REGISTRA (la composition root). Mientras fueron literales en cada slice, el
/// acuerdo era una coincidencia de cadenas que ningún compilador comprobaba, y
/// el castigo por una errata era una traza vacía en el dashboard.
/// </summary>
public static class TelemetrySources
{
    public const string Catalog = "ElGuerre.Tendero.Catalog";
    public const string Search = "ElGuerre.Tendero.Search";
    public const string Outbox = "ElGuerre.Tendero.Outbox";

    public static readonly string[] All = [Catalog, Search, Outbox];
}
