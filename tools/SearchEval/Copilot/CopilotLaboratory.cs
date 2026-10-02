using System.Text.Json;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Assist;
using ElGuerre.Tendero.Assist.Features.DecideProposals;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Catalog.Features.ListProducts;
using ElGuerre.Tendero.Inventory;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.SearchEval.Copilot;

/// <summary>Every read a turn made, as the model asked for it; cleared between cases.</summary>
public sealed class ToolCallLog
{
    private readonly List<ToolCall> calls = [];

    public IReadOnlyList<ToolCall> Calls
    {
        get
        {
            lock (this.calls)
                return [.. this.calls];
        }
    }

    public void Add(ToolCall call)
    {
        lock (this.calls)
            this.calls.Add(call);
    }

    public void Clear()
    {
        lock (this.calls)
            this.calls.Clear();
    }
}

/// <summary>
/// What the shop logged as a warning or worse while a case ran: why a turn was
/// unavailable — the turn answers so, and logs the reason, which the report shows.
/// </summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly List<string> lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (this.lines)
                return [.. this.lines];
        }
    }

    public void Clear()
    {
        lock (this.lines)
            this.lines.Clear();
    }

    public ILogger CreateLogger(string categoryName) => new Capture(this);

    public void Dispose() { }

    private sealed class Capture(CapturedLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            lock (owner.lines)
                owner.lines.Add($"{formatter(state, exception)}{(exception is null ? "" : $": {exception.GetType().Name}: {exception.Message}")}");
        }
    }
}

/// <summary>A context's read, recorded on its way in: the arguments are what the suite judges.</summary>
public sealed class RecordingReadTool(IReadTool inner, ToolCallLog log) : IReadTool
{
    public string Name => inner.Name;

    public string Description => inner.Description;

    public JsonElement ArgumentsSchema => inner.ArgumentsSchema;

    public Task<ReadAnswer> ReadAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        log.Add(new ToolCall(inner.Name, arguments.Clone()));
        return inner.ReadAsync(arguments, cancellationToken);
    }
}

/// <summary>
/// The shop the copilot is examined in (S-6): the real contexts, Assist and the
/// model port, composed as the API composes them, over a Postgres the operator
/// gives — empty or the laboratory's already — which it migrates and fills with the
/// seed catalogue and its stock. Every product has its English name in the seed, so
/// the cases that ask for gaps are asked of a catalogue that has none: what is
/// judged is the read the model chose, not what the read found.
/// </summary>
public static class CopilotLaboratory
{
    /// <summary>The profile the suite configures and the turn asks.</summary>
    public const string Profile = "evaluated";

    public static ServiceProvider Compose(string database, IReadOnlyDictionary<string, string?> profile, string repositoryRoot)
    {
        var seed = (string file) => Path.Combine(repositoryRoot, "seed", file);
        var settings = new Dictionary<string, string?>(profile)
        {
            ["Catalog:Connectors:Seed:FilePath"] = seed("products.sample.json"),
            ["Catalog:Attributes:FilePath"] = seed("attributes.sample.json"),
            ["Catalog:Categories:FilePath"] = seed("categories.sample.json"),
            ["Inventory:Warehouses:FilePath"] = seed("warehouses.sample.json"),
            ["Shop:TimeZone"] = "Europe/Madrid",
            ["Steward:Model"] = Profile,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        var logs = new CapturedLogs();
        services.AddSingleton(logs);
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddTenderoCqrs(
            typeof(ImportProductsCommand).Assembly,
            typeof(GetSalesSummaryQuery).Assembly,
            typeof(IStockLedger).Assembly,
            typeof(ApproveProposalsCommand).Assembly);
        services.AddTenderoPersistence(database);
        services.AddCatalog(configuration);
        services.AddInventory(configuration);
        services.AddOrdering(configuration);
        services.AddAssist(configuration);
        services.AddModelProfiles(configuration);
        services.AddSingleton<IPrincipalAccessor>(new Examiner());
        services.AddSingleton<ToolCallLog>();
        RecordReads(services);
        return services.BuildServiceProvider();
    }

    /// <summary>Migrates the schema and, on an empty shop, imports the seed catalogue and its stock.</summary>
    public static async Task PrepareAsync(ServiceProvider provider, string repositoryRoot, CancellationToken cancellationToken)
    {
        await provider.MigrateTenderoSchemaAsync(cancellationToken);
        if (await ProductsAsync(provider, cancellationToken) is { Count: > 0 })
            return;

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new ImportProductsCommand("seed"), cancellationToken);

        await using var stockScope = provider.CreateAsyncScope();
        var ledger = stockScope.ServiceProvider.GetRequiredService<IStockLedger>();
        await using var file = File.OpenRead(Path.Combine(repositoryRoot, "seed", "stock.sample.json"));
        foreach (var row in await JsonSerializer.DeserializeAsync<List<SeedStock>>(file, CopilotScoring.Json, cancellationToken) ?? [])
        {
            if (row.OnHand > 0)
                await ledger.ReceiveAsync(row.Sku, row.Warehouse, row.OnHand, cancellationToken);
            else
                await ledger.CountAsync(row.Sku, row.Warehouse, 0, cancellationToken);
        }
    }

    /// <summary>Each product's English name and the code the import gave it.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> ProductsAsync(ServiceProvider provider, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var page = await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
            .SendAsync(new ListProductsQuery(null, "en", 1, Paging.MaxPageSize), cancellationToken);
        return page.Items.ToDictionary(item => item.Name, item => item.Code, StringComparer.Ordinal);
    }

    /// <summary>Every context's read, wrapped so its calls are recorded.</summary>
    private static void RecordReads(IServiceCollection services)
    {
        foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IReadTool)).ToList())
        {
            services.Remove(descriptor);
            services.Add(new ServiceDescriptor(typeof(IReadTool), provider =>
            {
                var inner = descriptor.ImplementationFactory?.Invoke(provider)
                            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
                return new RecordingReadTool((IReadTool)inner, provider.GetRequiredService<ToolCallLog>());
            }, descriptor.Lifetime));
        }
    }

    private sealed record SeedStock(string Sku, string Warehouse, int OnHand);

    /// <summary>The shopkeeper the cases are asked as: no person, a role.</summary>
    private sealed class Examiner : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = new(null, null, "copilot-eval", "Copilot evaluation", ["shopkeeper"]);
    }
}
