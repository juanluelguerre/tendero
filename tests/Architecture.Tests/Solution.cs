extern alias TenderoApi;

using System.Reflection;
using Tendero.Catalog.Domain;
using Tendero.Ordering.Domain;
using Tendero.Persistence;
using Tendero.Search.Contracts;
using Tendero.ServiceDefaults;
using Tendero.SharedKernel;

namespace Tendero.Architecture.Tests;

/// <summary>
/// Los ensamblados que componen la solución. Se referencian por un tipo real y
/// no por nombre: si alguien renombra un proyecto, esto deja de compilar en vez
/// de dejar de comprobar nada en silencio.
/// </summary>
internal static class Solution
{
    public static readonly Assembly SharedKernel = typeof(LocalizedText).Assembly;
    public static readonly Assembly Catalog = typeof(Product).Assembly;
    public static readonly Assembly Ordering = typeof(Order).Assembly;
    public static readonly Assembly Search = typeof(IProductIndexer).Assembly;
    public static readonly Assembly Persistence = typeof(TenderoDbContext).Assembly;
    public static readonly Assembly ServiceDefaults = typeof(TelemetrySources).Assembly;
    public static readonly Assembly Api = typeof(TenderoApi::Program).Assembly;
    public static readonly Assembly Workers = typeof(Workers.OutboxProcessor).Assembly;

    /// <summary>Contextos y capacidades: donde viven dominio y slices.</summary>
    public static readonly Assembly[] Contexts = [Catalog, Ordering, Search];

    public static readonly Assembly[] All =
        [SharedKernel, Catalog, Ordering, Search, Persistence, ServiceDefaults, Api, Workers];
}
