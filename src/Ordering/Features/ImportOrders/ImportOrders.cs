using System.Diagnostics;
using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Ordering.Features.ImportOrders;

/// <param name="Culture">The language the notes are said in, picked as every endpoint picks it (ADR 0013).</param>
/// <param name="Rehearse">Read the source and answer what would come, with nothing written (phase 16, P16-13's rule).</param>
public sealed record ImportOrdersCommand(string Source, string Culture = "es", bool Rehearse = false) : ICommand<ImportOrdersResult>;

/// <param name="Created">Orders born in their final state.</param>
/// <param name="AlreadyImported">Orders a previous import brought, found by the source's id.</param>
/// <param name="Refunds">Refunds born as returns, closed.</param>
/// <param name="GiftCards">The store's gift cards still owed, which cannot come across: listed for the merchant to reissue (decision 6).</param>
public sealed record ImportOrdersResult(int Created, int AlreadyImported, int Refunds, IReadOnlyList<Message<OrderNoteCode>> Notes, IReadOnlyList<GiftCardToReissue> GiftCards);

/// <param name="LastCharacters">The end of its code: what the holder recognises it by. The store keeps the rest.</param>
/// <param name="Customer">The customer imported for its holder, when the import brought them.</param>
/// <param name="CustomerAtSource">The store's id for its holder, when it had one: whom to reissue it to when they were not imported.</param>
public sealed record GiftCardToReissue(string LastCharacters, Money Balance, DateOnly? ExpiresOn, string? Customer, string? CustomerAtSource);

public sealed class ImportOrdersValidator : AbstractValidator<ImportOrdersCommand>
{
    /// <summary>The source must exist, not just look like one: the catalogue's lesson.</summary>
    public ImportOrdersValidator(IOrderSourceRegistry sources)
    {
        RuleFor(command => command.Source)
            .NotEmpty()
            .Must(source => sources.Sources.Contains(source, StringComparer.Ordinal))
            .WithMessage(command =>
                $"Unknown order source '{command.Source}'. Registered sources: {string.Join(", ", sources.Sources)}.");
    }
}

public sealed class ImportOrdersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/orders/import  { "source": "shopify" }, or with "rehearse": true
        app.MapPost("/api/orders/import",
                async Task<Ok<ImportOrdersResult>> (
                    ImportOrdersCommand command, string? culture, HttpContext http,
                    ICommandDispatcher dispatcher, CancellationToken ct) =>
                {
                    var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    var result = await dispatcher.SendAsync(command with { Culture = language }, ct);
                    http.Response.Headers.ContentLanguage = language;
                    http.Response.Headers.Vary = "Accept-Language";
                    return TypedResults.Ok(result);
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Ordering")
            .WithName("ImportOrders");
    }
}

/// <summary>
/// A store's order history into the shop (phase 16, S2b, P16-18; ADR 0066). Each
/// closed order is born in its final state through <see cref="Order.Imported"/>:
/// its lines resolved to the catalogue by SKU, keeping the names the buyer saw;
/// owned by the customer Accounts imported for the buyer, or by a guest; its taxes
/// named as the store named them. Each refund of a delivered order is a return born
/// refunded, with no reason stated. Nothing moves stock, captures money or writes to
/// anybody.
///
/// An order still on its way is finished in the store first; an order naming
/// something the catalogue lacks waits for the catalogue's import rather than being
/// invented; figures that do not add up are refused. Each is said. The store's
/// gift cards cannot come across, so the answer lists them (P16-20).
/// </summary>
public sealed class ImportOrdersHandler(
    IOrderSourceRegistry sources,
    IGiftCardSourceRegistry giftCards,
    IOrderRepository orders,
    IReturnRequestRepository returns,
    ICatalogueVariants catalogue,
    IImportedCustomerIds customers,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ImportOrdersCommand, ImportOrdersResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<ImportOrdersResult> HandleAsync(ImportOrdersCommand command, CancellationToken cancellationToken)
    {
        var source = sources.Get(command.Source);

        using var activity = Telemetry.StartActivity("ordering.import");
        activity?.SetTag("catalog.source", source.Source);
        activity?.SetTag("ordering.import.rehearsal", command.Rehearse);

        var notes = new List<OrderNote>();
        int created = 0, alreadyImported = 0, refunds = 0;

        await foreach (var placed in source.StreamOrdersAsync(cancellationToken))
        {
            var origin = new OrderOrigin(source.Source, placed.ExternalId, placed.Number);
            if (await orders.FindByIdempotencyKeyAsync($"import:{origin.Source}:{origin.ExternalId}", cancellationToken) is not null)
            {
                alreadyImported++;
                continue;
            }

            if (placed.State == SourceOrderState.Open)
            {
                notes.Add(new OrderNote(OrderNoteCode.StillOpen, placed.Number, null));
                continue;
            }

            if (await LinesAsync(placed, notes, cancellationToken) is not { } lines)
                continue;

            var owner = await OwnerAsync(source.Source, placed, notes, cancellationToken);
            var order = Build(origin, placed, owner, lines, notes);
            if (order is null)
                continue;

            var born = Refunds(order, placed, source.Source, notes);
            created++;
            refunds += born.Count;

            // A rehearsal builds what the import would, so a refusal shows now, and keeps nothing.
            if (command.Rehearse)
                continue;

            orders.Add(order);
            foreach (var refund in born)
                returns.Add(refund);
        }

        if (!command.Rehearse && created > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        notes.AddRange(source.SourceNotes);
        var cards = await GiftCardsAsync(source.Source, cancellationToken);
        if (!command.Rehearse)
            ImportOrdersMetrics.Record(source.Source, created, alreadyImported, refunds, notes, cards.Count);

        var english = command.Culture == "en";
        return new ImportOrdersResult(created, alreadyImported, refunds, [.. notes.Select(note => ImportOrdersNotes.Said(note, english))], cards);
    }

    /// <summary>
    /// The store's gift cards still owed, listed and not imported (decision 6): read
    /// alike by a rehearsal and an import, since listing writes nothing. Each names
    /// the customer imported for its holder, or the store's id for them.
    /// </summary>
    private async Task<List<GiftCardToReissue>> GiftCardsAsync(string source, CancellationToken cancellationToken)
    {
        if (giftCards.Find(source) is not { } connector)
            return [];

        var cards = new List<SourceGiftCard>();
        await foreach (var card in connector.StreamGiftCardsAsync(cancellationToken))
            cards.Add(card);

        var holders = cards.Select(card => card.CustomerExternalId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var imported = holders.Count == 0 ? new Dictionary<string, CustomerId>() : await customers.ForAsync(source, holders, cancellationToken);

        return
        [
            .. cards.Select(card => new GiftCardToReissue(
                card.LastCharacters,
                card.Balance,
                card.ExpiresOn,
                card.CustomerExternalId is { } holder && imported.TryGetValue(holder, out var customer) ? customer.Value.ToString() : null,
                card.CustomerExternalId)),
        ];
    }

    /// <summary>The order's lines as the catalogue sells them, or null — with a note — when one names something it lacks.</summary>
    private async Task<List<OrderLine>?> LinesAsync(SourceOrder placed, List<OrderNote> notes, CancellationToken cancellationToken)
    {
        var (lines, missing) = await SourceOrders.LinesAsync(placed, catalogue, cancellationToken);
        if (missing is not null)
            notes.Add(new OrderNote(OrderNoteCode.ProductNotInCatalogue, placed.Number, missing.Sku ?? missing.ProductName));

        return lines;
    }

    /// <summary>The customer imported for the buyer, or a guest — said, when the store knew who they were.</summary>
    private async Task<CustomerId> OwnerAsync(string source, SourceOrder placed, List<OrderNote> notes, CancellationToken cancellationToken)
    {
        if (placed.CustomerExternalId is not { } buyer)
            return CustomerId.New();

        var imported = await customers.ForAsync(source, [buyer], cancellationToken);
        if (imported.TryGetValue(buyer, out var customer))
            return customer;

        notes.Add(new OrderNote(OrderNoteCode.CustomerNotImported, placed.Number, buyer));
        return CustomerId.New();
    }

    /// <summary>The order born closed, or null — with a note — when the store's figures do not make one.</summary>
    private static Order? Build(OrderOrigin origin, SourceOrder placed, CustomerId owner, List<OrderLine> lines, List<OrderNote> notes)
    {
        var shipping = placed.ShippingAddress ?? placed.BillingAddress;
        var billing = placed.BillingAddress ?? placed.ShippingAddress;
        var totals = placed.Totals;

        try
        {
            if (shipping is null || billing is null)
                throw new InvalidOperationException("An order needs somewhere it was sent or billed to.");

            return Order.Imported(
                origin,
                placed.State == SourceOrderState.Fulfilled ? OrderStatus.Delivered : OrderStatus.Cancelled,
                owner,
                lines,
                shipping,
                billing,
                new OrderShipping($"{origin.Source}:shipping", placed.Shipping.Label, placed.Shipping.Amount, null),
                new OrderTotals(totals.Subtotal, totals.Discount, totals.Shipping, totals.Tax, totals.Total),
                placed.PlacedAt,
                [.. placed.Discounts.Select(discount => new OrderDiscount(discount.Code, discount.Label, discount.Amount))],
                [.. placed.Taxes.Select(SourceOrders.Tax)],
                placed.Culture ?? "es",
                placed.ShippedAt,
                placed.ContactEmail);
        }
        catch (InvalidOperationException refused)
        {
            notes.Add(new OrderNote(OrderNoteCode.TotalsDoNotAddUp, placed.Number, refused.Message));
            return null;
        }
    }

    /// <summary>
    /// Each refund of a delivered order, a return born refunded with no reason stated.
    /// A cancelled order's refund is the cancellation itself; one the shop cannot
    /// express as a return — no line, a line it cannot find — is said.
    /// </summary>
    private static List<ReturnRequest> Refunds(Order order, SourceOrder placed, string source, List<OrderNote> notes)
    {
        var born = new List<ReturnRequest>();
        if (order.Status != OrderStatus.Delivered)
            return born;

        foreach (var refund in placed.Refunds)
        {
            try
            {
                var lines = refund.Lines.Select(returned =>
                {
                    var index = placed.Lines.ToList().FindIndex(line => line.ExternalId == returned.LineExternalId);
                    if (index < 0)
                        throw new InvalidOperationException($"no line {returned.LineExternalId}");

                    var line = order.Lines[index];
                    return new ReturnLine(line.VariantId, line.Sku, returned.Quantity, ReturnReason.Unstated, null);
                }).ToList();

                born.Add(ReturnRequest.Imported(order, lines, born, refund.Amount, $"{source}:refund:{refund.ExternalId}", refund.At));
            }
            catch (InvalidOperationException unexpressed)
            {
                notes.Add(new OrderNote(OrderNoteCode.RefundNotExpressed, placed.Number, unexpressed.Message));
            }
        }

        return born;
    }
}

/// <summary>An order note as the answer says it: the code, what it is about, and the sentence in the language asked.</summary>
internal static class ImportOrdersNotes
{
    public static Message<OrderNoteCode> Said(OrderNote note, bool english) =>
        new(note.Code, note.About, Sentences.StartingUpper(Sentence(note, english)));

    private static string Sentence(OrderNote note, bool english)
    {
        var about = note.About is null ? "" : $"{note.About}: ";
        var detail = note.Detail ?? "";

        return note.Code switch
        {
            OrderNoteCode.StillOpen => english
                ? $"{about}the order is still on its way; finish it in the store before switching. It was not imported."
                : $"{about}el pedido aún está en curso; termínalo en la tienda antes del cambio. No se ha importado.",
            OrderNoteCode.ProductNotInCatalogue => english
                ? $"{about}it names {detail}, which the catalogue does not have; import the catalogue first. It was not imported."
                : $"{about}incluye {detail}, que no está en el catálogo; importa antes el catálogo. No se ha importado.",
            OrderNoteCode.CustomerNotImported => english
                ? $"{about}its buyer ({detail}) was not imported; the order came, owned by a guest."
                : $"{about}su comprador ({detail}) no se ha importado; el pedido ha venido como de un invitado.",
            OrderNoteCode.TotalsDoNotAddUp => english
                ? $"{about}its figures do not make an order ({detail}); it was not imported."
                : $"{about}sus importes no cuadran como pedido ({detail}); no se ha importado.",
            OrderNoteCode.RefundNotExpressed => english
                ? $"{about}a refund could not be expressed as a return ({detail}); the order came without it."
                : $"{about}un reembolso no se ha podido expresar como devolución ({detail}); el pedido ha venido sin él.",
        };
    }
}

/// <summary>
/// `tendero.ordering.import.orders`, `tendero.ordering.import.notes` and
/// `tendero.ordering.import.gift_cards` (the tracking plan; phase 16, spec
/// *Evidence of use*): how much of a store's history comes across, and how many
/// gift cards are left for the merchant to reissue, by source, labelled as the
/// other contexts' imports are.
/// </summary>
internal static class ImportOrdersMetrics
{
    private static readonly Meter Meter = new(TelemetrySources.Ordering);

    private static readonly Counter<long> Orders = Meter.CreateCounter<long>(
        "tendero.ordering.import.orders", unit: "{order}", description: "Orders an import created or found already imported, and refunds it brought, by source.");

    private static readonly Counter<long> Notes = Meter.CreateCounter<long>(
        "tendero.ordering.import.notes", unit: "{note}", description: "What an order source did not hand over cleanly, by source and code.");

    private static readonly Counter<long> GiftCards = Meter.CreateCounter<long>(
        "tendero.ordering.import.gift_cards", unit: "{card}", description: "Gift cards an import listed for the merchant to reissue, by source.");

    public static void Record(string source, int created, int alreadyImported, int refunds, IEnumerable<OrderNote> notes, int giftCards)
    {
        Add(source, "created", created);
        Add(source, "already_imported", alreadyImported);
        Add(source, "refund", refunds);
        if (giftCards > 0)
            GiftCards.Add(giftCards, new KeyValuePair<string, object?>("catalog.source", source));

        foreach (var group in notes.GroupBy(note => note.Code))
        {
            Notes.Add(
                group.Count(),
                new KeyValuePair<string, object?>("catalog.source", source),
                new KeyValuePair<string, object?>("ordering.import.note", group.Key.ToString()));
        }
    }

    private static void Add(string source, string outcome, int count)
    {
        if (count == 0)
            return;

        Orders.Add(
            count,
            new KeyValuePair<string, object?>("catalog.source", source),
            new KeyValuePair<string, object?>("ordering.import.outcome", outcome));
    }
}
