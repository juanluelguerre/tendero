extern alias TenderoApi;
using System.Reflection;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// The assemblies that make up the solution. Referenced by a real type and not
/// by name: if somebody renames a project, this stops compiling instead of
/// silently stopping checking anything.
/// </summary>
internal static class Solution
{
    public static readonly Assembly SharedKernel = typeof(LocalizedText).Assembly;
    public static readonly Assembly Catalog = typeof(Product).Assembly;
    public static readonly Assembly Ordering = typeof(Order).Assembly;
    public static readonly Assembly Search = typeof(IProductIndexer).Assembly;
    public static readonly Assembly Inventory = typeof(StockItem).Assembly;
    public static readonly Assembly Pricing = typeof(Promotion).Assembly;
    public static readonly Assembly Persistence = typeof(TenderoDbContext).Assembly;
    public static readonly Assembly ServiceDefaults = typeof(HostingExtensions).Assembly;
    public static readonly Assembly Api = typeof(TenderoApi::Program).Assembly;
    public static readonly Assembly Workers = typeof(Workers.OutboxProcessor).Assembly;
    public static readonly Assembly SearchEval = typeof(SearchEval.RelevanceMetrics).Assembly;

    /// <summary>Contextos y capacidades: donde viven dominio y slices.</summary>
    public static readonly Assembly[] Contexts = [Catalog, Inventory, Ordering, Pricing, Search];

    public static readonly Assembly[] All =
    [
        SharedKernel, Catalog, Inventory, Ordering, Pricing, Search,
        Persistence, ServiceDefaults, Api, Workers, SearchEval
    ];
}
