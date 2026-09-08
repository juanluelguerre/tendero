extern alias TenderoApi;
using System.Reflection;
using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Mcp;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Ucp;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// The assemblies that make up the solution. Referenced by a real type and not
/// by name: if somebody renames a project, this stops compiling instead of
/// silently stopping checking anything.
/// </summary>
internal static class Solution
{
    public static readonly Assembly SharedKernel = typeof(LocalizedText).Assembly;
    public static readonly Assembly Accounts = typeof(Customer).Assembly;
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

    public static readonly Assembly Mcp = typeof(TenderoMcpServer).Assembly;
    public static readonly Assembly Ucp = typeof(UcpProfile).Assembly;

    /// <summary>Contexts and capabilities: where the domain and the slices live.</summary>
    public static readonly Assembly[] Contexts = [Accounts, Catalog, Inventory, Ordering, Pricing, Search];

    /// <summary>
    /// Transports, which are deliberately NOT in <see cref="Contexts"/>: they own
    /// no state, declare no domain and answer no query of their own. What they do
    /// is carry an existing slice over a second protocol, and the rules below are
    /// what keep them at that. `Ucp` joined in P9-5, before it had an endpoint: a rule that arrives after the code is a rule the code was not written against.
    /// </summary>
    public static readonly Assembly[] Transports = [Mcp, Ucp];

    public static readonly Assembly[] All =
    [
        SharedKernel, Accounts, Catalog, Inventory, Ordering, Pricing, Search,
        Mcp, Ucp, Persistence, ServiceDefaults, Api, Workers, SearchEval
    ];
}
