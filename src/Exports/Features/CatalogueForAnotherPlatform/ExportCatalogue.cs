using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Exports.Features.ProductFeed;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Exports.Features.CatalogueForAnotherPlatform;

/// <param name="Format">The platform's format: "shopify-csv".</param>
/// <param name="ImagesBaseUrl">Where the API answering is reached from outside: the other platform fetches the pictures from it.</param>
public sealed record ExportCatalogueCommand(string Format, string Culture, string ImagesBaseUrl) : ICommand<ExportFile>;

public sealed class ExportCatalogueValidator : AbstractValidator<ExportCatalogueCommand>
{
    public ExportCatalogueValidator(IExportFormats formats)
    {
        RuleFor(command => command.Format)
            .Must(format => formats.Find<ProductForAnotherPlatform>(format) is not null)
            .WithMessage($"Supported formats: {ShopifyProductCsv.Key}.");
        RuleFor(command => command.Culture).Must(culture => culture is "es" or "en").WithMessage("Supported cultures: es, en.");
    }
}

public sealed class ExportCatalogueEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/exports/catalogue?format=shopify-csv&culture=es
        //
        // A command, so the audit log says who took the catalogue away and in which
        // format (spec, *Who may export what*), and the shopkeeper's alone.
        app.MapPost("/api/exports/catalogue",
                async (string format, string? culture, HttpContext http, ICommandDispatcher commands, CancellationToken ct) =>
                {
                    var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    var file = await commands.SendAsync(new ExportCatalogueCommand(format, language, $"{http.Request.Scheme}://{http.Request.Host}"), ct);
                    return Results.File(file.Content, file.ContentType, file.FileName);
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Exports")
            .WithName("ExportCatalogue")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv");
    }
}

/// <summary>
/// The catalogue for another platform (phase 16, E-3; spec, *The catalogue for another
/// platform*): every live product — on sale, and in Draft as a draft — with its
/// variants, what can still be sold of each, its pictures and Google's category, in
/// the format the platform imports. It closes phase 16's round trip from the other
/// side, and it is the proof that a merchant can leave Tendero as they left Shopify.
/// </summary>
public sealed class ExportCatalogueHandler(
    ICatalogueExport catalogue, IAvailabilityReader availability, GoogleCategoryMapping categories, IExportFormats formats)
    : ICommandHandler<ExportCatalogueCommand, ExportFile>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Exports);

    public async Task<ExportFile> HandleAsync(ExportCatalogueCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("exports.catalogue");
        activity?.SetTag("export.format", command.Format);
        activity?.SetTag("export.culture", command.Culture);

        var format = formats.Find<ProductForAnotherPlatform>(command.Format)
            ?? throw new InvalidOperationException($"No {command.Format} format is registered for the catalogue; the validator lets none through.");

        var products = await catalogue.ProductsAsync(command.Culture, cancellationToken);
        var skus = products.SelectMany(product => product.Variants).Select(variant => variant.Sku).ToList();
        var available = skus.Count == 0 ? new Dictionary<string, int>() : await availability.AvailableAsync(skus, cancellationToken);
        string Picture(string id) => $"{command.ImagesBaseUrl.TrimEnd('/')}/api/images/{id}";

        var rows = products.Select(product => new ProductForAnotherPlatform(
            $"{product.Slug}-{product.Code.ToLowerInvariant()}",
            product.Name,
            product.Description,
            product.Brand,
            product.Category.Count == 0 ? null : product.Category[^1].Name,
            product.OnSale,
            [.. product.Variants.FirstOrDefault()?.Options.Select(option => option.AxisLabel) ?? []],
            [
                .. product.Variants.Select(variant => new VariantForAnotherPlatform(
                    variant.Sku,
                    variant.Barcode,
                    variant.Price,
                    [.. variant.Options.Select(option => option.Label)],
                    available.GetValueOrDefault(variant.Sku),
                    variant.PictureIds.Count > 0 ? Picture(variant.PictureIds[0]) : null)),
            ],
            [.. product.Pictures.Select(picture => new PictureForAnotherPlatform(Picture(picture.ImageId), picture.Alt))],
            categories.Of(product.Category))).ToList();

        using var output = new MemoryStream();
        await format.WriteAsync(rows, new ExportWriting(command.Culture), output, cancellationToken);

        activity?.SetTag("export.rows", rows.Count);
        ExportMetrics.Record(ExportKind.Catalogue, format.Format, rows.Count);
        return new ExportFile(output.ToArray(), format.ContentType, $"catalogue-{command.Culture}.{format.Extension}", rows.Count);
    }
}
