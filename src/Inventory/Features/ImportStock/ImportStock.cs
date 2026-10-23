using Carter;
using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Inventory.Features.ImportStock;

/// <param name="Culture">The language the notes are said in, picked as every
/// endpoint picks it (ADR 0013).</param>
public sealed record ImportStockCommand(string Source, string Culture = "es") : ICommand<ImportStockResult>;

/// <summary>A place of the source the shop does not operate, for a person to add to the warehouses file.</summary>
public sealed record ProposedWarehouse(string Code, string Name, bool IsActive);

/// <param name="Counted">Levels entered as counts.</param>
/// <param name="Skipped">Levels in a warehouse the shop does not operate.</param>
/// <param name="ProposedWarehouses">The source's places the shop does not have:
/// warehouses are a file nothing writes, so they are proposed, never created
/// (phase 16, S2a, spec corrected).</param>
public sealed record ImportStockResult(
    int Counted,
    int Skipped,
    IReadOnlyList<ProposedWarehouse> ProposedWarehouses,
    IReadOnlyList<Message<StockNoteCode>> Notes);

public sealed class ImportStockValidator : AbstractValidator<ImportStockCommand>
{
    /// <summary>The source must exist, not just look like one: the catalogue's lesson.</summary>
    public ImportStockValidator(IStockSourceRegistry sources)
    {
        RuleFor(command => command.Source)
            .NotEmpty()
            .Must(source => sources.Sources.Contains(source, StringComparer.Ordinal))
            .WithMessage(command =>
                $"Unknown stock source '{command.Source}'. Registered sources: {string.Join(", ", sources.Sources)}.");
    }
}

public sealed class ImportStockEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/inventory/import  { "source": "shopify" }
        app.MapPost("/api/inventory/import",
                async Task<Ok<ImportStockResult>> (
                    ImportStockCommand command, string? culture, HttpContext http,
                    ICommandDispatcher dispatcher, CancellationToken ct) =>
                {
                    var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    var result = await dispatcher.SendAsync(command with { Culture = language }, ct);
                    http.Response.Headers.ContentLanguage = language;
                    http.Response.Headers.Vary = "Accept-Language";
                    return TypedResults.Ok(result);
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Inventory")
            .WithName("ImportStock");
    }
}

/// <summary>
/// A source's stock into the ledger (phase 16, S2a, P16-10). Each level is a
/// **count** — what the shelf holds, whatever the system thought — because a
/// migration is a stocktake, and only a count can say zero (IStockLedger). Only
/// in a warehouse the shop operates: a level elsewhere would create a shelf in a
/// building that does not exist, so it is said instead, and its place proposed.
/// </summary>
public sealed class ImportStockHandler(
    IStockSourceRegistry sources, IStockLedger ledger, IWarehouseReader warehouses)
    : ICommandHandler<ImportStockCommand, ImportStockResult>
{
    public async Task<ImportStockResult> HandleAsync(ImportStockCommand command, CancellationToken cancellationToken)
    {
        var source = sources.Get(command.Source);
        var operated = (await warehouses.AllAsync(cancellationToken))
            .Select(warehouse => warehouse.Code)
            .ToHashSet(StringComparer.Ordinal);

        var notes = new List<StockNote>();
        int counted = 0, skipped = 0;

        await foreach (var level in source.StreamLevelsAsync(cancellationToken))
        {
            var code = Warehouse.Normalise(level.WarehouseCode);
            if (!operated.Contains(code))
            {
                notes.Add(new StockNote(StockNoteCode.WarehouseUnknown, level.Sku, code));
                skipped++;
                continue;
            }

            await ledger.CountAsync(level.Sku, code, level.OnHand, cancellationToken);
            counted++;
        }

        var proposed = new List<ProposedWarehouse>();
        await foreach (var warehouse in source.StreamWarehousesAsync(cancellationToken))
        {
            if (!operated.Contains(Warehouse.Normalise(warehouse.Code)))
                proposed.Add(new ProposedWarehouse(Warehouse.Normalise(warehouse.Code), warehouse.Name, warehouse.IsActive));
        }

        notes.AddRange(source.SourceNotes);
        var english = command.Culture == "en";

        return new ImportStockResult(counted, skipped, proposed, [.. notes.Select(note => ImportStockNotes.Said(note, english))]);
    }
}

/// <summary>A stock note as the answer says it: the code, what it is about, and the sentence in the language asked.</summary>
internal static class ImportStockNotes
{
    public static Message<StockNoteCode> Said(StockNote note, bool english) =>
        new(note.Code, note.About, Sentence(note, english));

    private static string Sentence(StockNote note, bool english)
    {
        var about = note.About is null ? "" : $"{note.About}: ";
        var detail = note.Detail ?? "";

        return note.Code switch
        {
            StockNoteCode.LocationNotMapped => english
                ? $"The location “{detail}” has no warehouse code here; its stock was not read."
                : $"La ubicación «{detail}» no tiene código de almacén aquí; su stock no se ha leído.",
            StockNoteCode.VariantNotInCatalogue => english
                ? $"{about}the source counts variant {detail}, which is not in the catalogue; import the catalogue first."
                : $"{about}el origen cuenta la variante {detail}, que no está en el catálogo; importa antes el catálogo.",
            StockNoteCode.WarehouseUnknown => english
                ? $"{about}the warehouse {detail} is not one this shop operates; its count was not entered."
                : $"{about}el almacén {detail} no es uno de los de esta tienda; su recuento no se ha anotado.",
        };
    }
}
