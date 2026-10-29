using System.Diagnostics;
using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.Accounts.Features.ImportCustomers;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Inventory.Features.ImportStock;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering.Features.ImportOrders;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Pricing.Features.ProposePromotions;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Api;

/// <summary>A migration's steps, in the order they run: each later one reads what the earlier ones bring.</summary>
public enum MigrationStep
{
    Catalogue,
    Stock,
    Customers,
    Orders,
    Discounts,
}

/// <param name="Source">The store being left: "shopify"…</param>
/// <param name="Rehearse">Run every step with nothing written, and answer what would come (spec, *The dry run, then the migration*).</param>
public sealed record MigrateStoreRequest(string Source, bool Rehearse = false);

/// <summary>
/// One answer across the contexts: each step's own, as its import answers it, and
/// the steps the source has no adapter for. Gift cards travel in the orders'.
/// </summary>
/// <param name="NotBrought">The steps the source cannot hand over: no adapter of that port is registered for it.</param>
public sealed record StoreMigrationResult(
    string Source,
    bool Rehearsal,
    ImportProductsResult? Catalogue,
    ImportStockResult? Stock,
    ImportCustomersResult? Customers,
    ImportOrdersResult? Orders,
    ProposePromotionsResult? Discounts,
    IReadOnlyList<MigrationStep> NotBrought);

public static class StoreMigrationRefusals
{
    public static readonly Refusal UnknownSource = new(
        "UnknownSource", 400,
        "Ninguna parte de la tienda se puede traer desde esa fuente.", "No part of the shop can be brought from that source.");
}

/// <summary>
/// A store's migration as one operation, run twice (phase 16, S2b, P16-21; spec
/// *The dry run, then the migration*): first as a rehearsal, which reads everything
/// and writes nothing, then for real. Each step is its context's own import,
/// dispatched as its endpoint would, in the order each needs the one before: the
/// catalogue, its stock, the customers, their orders, the discounts.
///
/// **One request, one scope**, and that is what makes the rehearsal worth reading.
/// A rehearsed catalogue and rehearsed customers are held for the scope
/// (<see cref="IRehearsedCatalogue"/>, <see cref="IRehearsedCustomers"/>), and the
/// readers the later steps use see them, so on an empty shop an order's lines
/// still find the products the catalogue's step would bring.
///
/// It lives in the host and not in a context because it composes five of them, and
/// only the host knows them all — the same reason <c>AddShopify</c> is composed here.
/// </summary>
public sealed class StoreMigration(
    ICommandDispatcher commands,
    ICatalogSourceRegistry catalogue,
    IStockSourceRegistry stock,
    ICustomerSourceRegistry customers,
    IOrderSourceRegistry orders,
    IDiscountSourceRegistry discounts)
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Migration);

    /// <summary>The steps this source has an adapter for.</summary>
    public IReadOnlyList<MigrationStep> StepsOf(string source) =>
    [
        .. new (MigrationStep Step, IReadOnlyCollection<string> Sources)[]
        {
            (MigrationStep.Catalogue, catalogue.Sources),
            (MigrationStep.Stock, stock.Sources),
            (MigrationStep.Customers, customers.Sources),
            (MigrationStep.Orders, orders.Sources),
            (MigrationStep.Discounts, discounts.Sources),
        }
        .Where(each => each.Sources.Contains(source, StringComparer.Ordinal))
        .Select(each => each.Step),
    ];

    public async Task<StoreMigrationResult> RunAsync(string source, string culture, bool rehearse, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("migration.run");
        activity?.SetTag("catalog.source", source);
        activity?.SetTag("migration.rehearsal", rehearse);

        var offered = StepsOf(source);

        async Task<T?> Step<T>(MigrationStep step, ICommand<T> command) where T : class =>
            offered.Contains(step) ? await commands.SendAsync(command, cancellationToken) : null;

        var result = new StoreMigrationResult(
            source,
            rehearse,
            await Step(MigrationStep.Catalogue, new ImportProductsCommand(source, culture, rehearse)),
            await Step(MigrationStep.Stock, new ImportStockCommand(source, culture, rehearse)),
            await Step(MigrationStep.Customers, new ImportCustomersCommand(source, culture, rehearse)),
            await Step(MigrationStep.Orders, new ImportOrdersCommand(source, culture, rehearse)),
            await Step(MigrationStep.Discounts, new ProposePromotionsCommand(source, culture, rehearse)),
            [.. Enum.GetValues<MigrationStep>().Except(offered)]);

        StoreMigrationMetrics.Record(source, rehearse);
        return result;
    }
}

public sealed class StoreMigrationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/migration  { "source": "shopify", "rehearse": true }
        app.MapPost("/api/migration",
                async Task<Results<Ok<StoreMigrationResult>, ProblemHttpResult>> (
                    MigrateStoreRequest request, string? culture, HttpContext http,
                    StoreMigration migration, CancellationToken ct) =>
                {
                    if (migration.StepsOf(request.Source).Count == 0)
                    {
                        return TypedResults.Problem(
                            statusCode: StoreMigrationRefusals.UnknownSource.Status,
                            extensions: StoreMigrationRefusals.UnknownSource.Said(path: "$.source"));
                    }

                    var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    var result = await migration.RunAsync(request.Source, language, request.Rehearse, ct);
                    http.Response.Headers.ContentLanguage = language;
                    http.Response.Headers.Vary = "Accept-Language";
                    return TypedResults.Ok(result);
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Migration")
            .WithName("MigrateStore")
            .WithMetadata(new Refuses(StoreMigrationRefusals.UnknownSource));
    }
}

/// <summary>
/// `tendero.migration.runs` (the tracking plan): each migration run, rehearsed or
/// for real, by source. A rehearsal never followed by a migration says the answer
/// stopped the merchant; that is what decides which step's notes to work on first.
/// </summary>
internal static class StoreMigrationMetrics
{
    private static readonly Meter Meter = new(TelemetrySources.Migration);

    private static readonly Counter<long> Runs = Meter.CreateCounter<long>(
        "tendero.migration.runs", unit: "{run}", description: "A store's migrations run as one operation, rehearsed or for real, by source.");

    public static void Record(string source, bool rehearse) =>
        Runs.Add(
            1,
            new KeyValuePair<string, object?>("catalog.source", source),
            new KeyValuePair<string, object?>("migration.rehearsal", rehearse ? "true" : "false"));
}
