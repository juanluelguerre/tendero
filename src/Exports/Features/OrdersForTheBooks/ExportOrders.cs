using System.Diagnostics;
using System.Globalization;
using Carter;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Exports.Features.OrdersForTheBooks;

/// <param name="From">The first of the shop's days, included.</param>
/// <param name="To">The last of the shop's days, included.</param>
public sealed record ExportOrdersCommand(DateOnly From, DateOnly To, string Format, string Culture) : ICommand<ExportFile>;

public sealed class ExportOrdersValidator : AbstractValidator<ExportOrdersCommand>
{
    public ExportOrdersValidator(IExportFormats formats)
    {
        RuleFor(command => command.To).GreaterThanOrEqualTo(command => command.From).WithMessage("The span ends on or after the day it starts.");
        RuleFor(command => command.Format)
            .Must(format => formats.Find<OrderLineForTheBooks>(format) is not null)
            .WithMessage($"Supported formats: {OrdersForTheBooksCsv.Key}.");
        RuleFor(command => command.Culture).Must(culture => culture is "es" or "en").WithMessage("Supported cultures: es, en.");
    }
}

public sealed class ExportOrdersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/exports/orders?from=2026-10-01&to=2026-10-31&format=csv
        //
        // A command, so the audit log keeps one row per export with who took which
        // span (spec, *Who may export what*): the file carries the billing names and
        // addresses an invoice keeps. The shopkeeper's alone.
        app.MapPost("/api/exports/orders",
                async (DateOnly from, DateOnly to, string? format, string? culture, HttpContext http, ICommandDispatcher commands, CancellationToken ct) =>
                {
                    var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    var file = await commands.SendAsync(new ExportOrdersCommand(from, to, format ?? OrdersForTheBooksCsv.Key, language), ct);
                    return Results.File(file.Content, file.ContentType, file.FileName);
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Exports")
            .WithName("ExportOrders")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv");
    }
}

/// <summary>
/// Orders for the accountant (phase 16, E-4; spec, *Orders for the accountant*): the
/// sales of a span of the shop's days, each order as the rows an accountant books — a
/// row per line, per discount (its amount negative, so the column sums to what the
/// order took), for the shipping when it cost something, and per tax rate with its
/// base. It reads only what the order froze for its invoice: the billing party and
/// country, never the buyer's email or phone, which an erased customer's orders keep
/// blocked for this use alone (P15-9).
/// </summary>
public sealed class ExportOrdersHandler(IOrderExport sales, IExportFormats formats) : ICommandHandler<ExportOrdersCommand, ExportFile>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Exports);

    public async Task<ExportFile> HandleAsync(ExportOrdersCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("exports.orders");
        activity?.SetTag("export.format", command.Format);
        activity?.SetTag("export.days", command.To.DayNumber - command.From.DayNumber + 1);

        var format = formats.Find<OrderLineForTheBooks>(command.Format)
            ?? throw new InvalidOperationException($"No {command.Format} format is registered for orders; the validator lets none through.");

        var rows = (await sales.SalesAsync(command.From, command.To, cancellationToken)).SelectMany(Rows).ToList();

        using var output = new MemoryStream();
        await format.WriteAsync(rows, new ExportWriting(command.Culture), output, cancellationToken);

        activity?.SetTag("export.rows", rows.Count);
        ExportMetrics.Record(ExportKind.Orders, format.Format, rows.Count);
        var span = $"{command.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}-{command.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        return new ExportFile(output.ToArray(), format.ContentType, $"orders-{span}.{format.Extension}", rows.Count);
    }

    private static IEnumerable<OrderLineForTheBooks> Rows(ExportedOrder order)
    {
        OrderLineForTheBooks Row(BookLineKind kind, string description) => new(
            order.OrderId, order.Day, order.Status, order.Customer, order.Country, kind, null, description, null, null, null, null, null, null);

        foreach (var line in order.Lines)
            yield return Row(BookLineKind.Line, line.Name) with { Sku = line.Sku, Quantity = line.Quantity, UnitPrice = line.UnitPrice, Amount = line.Total };

        foreach (var discount in order.Discounts)
            yield return Row(BookLineKind.Discount, $"{discount.Code} {discount.Label}") with { Amount = discount.Amount with { Amount = -discount.Amount.Amount } };

        if (order.Shipping.Amount.Amount != 0m)
            yield return Row(BookLineKind.Shipping, order.Shipping.Label) with { Amount = order.Shipping.Amount };

        foreach (var tax in order.Taxes)
            yield return Row(BookLineKind.Tax, tax.TaxClass) with { TaxRate = tax.Rate, TaxBase = tax.Base, Tax = tax.Amount };
    }
}
