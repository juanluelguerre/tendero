using System.Text.Json;
using ElGuerre.Tendero.Inventory.Ports;
using Microsoft.EntityFrameworkCore;
using ElGuerre.Tendero.Persistence;

namespace ElGuerre.Tendero.Workers;

public sealed class StockSeedOptions
{
    public const string SectionName = "Inventory:Stock";

    public string FilePath { get; set; } = Path.Combine("seed", "stock.sample.json");
}

/// <summary>
/// Puts the sample stock on the shelves, once, on a database that has none.
///
/// Stock is the first seed in this repository that cannot be a file read at
/// runtime. The warehouses, the attribute definitions, the tariffs and the
/// promotions are all read every time because nothing writes them; stock is
/// written by every order that is placed, so a file read on each request would
/// undo the shop's own bookkeeping between one page and the next.
///
/// So it is seeded, and it is seeded **through <see cref="IStockLedger.ReceiveAsync"/>**
/// rather than by inserting rows. Receiving is what actually happens when goods
/// arrive: it raises <c>StockLevelChanged</c>, the outbox carries it, and the
/// search documents get their <c>inStock</c> without a second mechanism. An
/// INSERT would put numbers in a table that nothing downstream ever heard about.
///
/// It runs **only when the table is empty**, which is what keeps it from
/// resetting a shop somebody has been using. And only in Development, for the
/// same reason <c>SchemaMigrator</c> is: seeding on startup is a deployment
/// decision, not a process one.
/// </summary>
public sealed class StockSeeder(
    IServiceScopeFactory scopes,
    IOptions<StockSeedOptions> options,
    IHostEnvironment environment,
    ILogger<StockSeeder> logger) : IHostedService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return;

        var path = options.Value.FilePath;
        if (!File.Exists(path))
        {
            logger.LogInformation("No stock seed at {Path}; the warehouses start empty", path);
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();

        if (await context.StockItems.AnyAsync(cancellationToken))
            return;

        await using var file = File.OpenRead(path);
        var rows = await JsonSerializer.DeserializeAsync<List<SeedStock>>(file, Json, cancellationToken) ?? [];

        var ledger = scope.ServiceProvider.GetRequiredService<IStockLedger>();

        foreach (var row in rows)
        {
            // A delivery and a stocktake are different facts, and only one of
            // them can say zero: receiving nothing is a delivery that did not
            // happen, while counting zero is a shelf somebody looked at.
            //
            // The seed deliberately carries both, because "we stock this here
            // and there are none" and "we do not stock this here" are different
            // refusals — and filtering the zeroes out, which is what this loop
            // used to do, collapsed them into one. The out-of-stock row on the
            // grid is the visible half of that distinction.
            if (row.OnHand > 0)
                await ledger.ReceiveAsync(row.Sku, row.Warehouse, row.OnHand, cancellationToken);
            else
                await ledger.CountAsync(row.Sku, row.Warehouse, 0, cancellationToken);
        }

        logger.LogInformation("Seeded {Count} stock rows from {Path}", rows.Count, path);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private sealed record SeedStock(string Sku, string Warehouse, int OnHand);
}
