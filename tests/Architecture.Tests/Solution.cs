extern alias TenderoApi;

using System.Reflection;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Architecture.Tests;

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
    public static readonly Assembly Pricing = typeof(Promotion).Assembly;
    public static readonly Assembly Persistence = typeof(TenderoDbContext).Assembly;
    public static readonly Assembly ServiceDefaults = typeof(HostingExtensions).Assembly;
    public static readonly Assembly Api = typeof(TenderoApi::Program).Assembly;
    public static readonly Assembly Workers = typeof(Workers.OutboxProcessor).Assembly;
    public static readonly Assembly SearchEval = typeof(SearchEval.RelevanceMetrics).Assembly;

    /// <summary>Contextos y capacidades: donde viven dominio y slices.</summary>
    public static readonly Assembly[] Contexts = [Catalog, Ordering, Pricing, Search];

    public static readonly Assembly[] All =
        [SharedKernel, Catalog, Ordering, Pricing, Search, Persistence, ServiceDefaults, Api, Workers, SearchEval];
}
