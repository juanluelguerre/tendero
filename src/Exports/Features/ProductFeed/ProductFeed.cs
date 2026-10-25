using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Exports.Features.ProductFeed;

/// <param name="ImagesBaseUrl">Where the API answering is reached from outside: a picture's address is built on it.</param>
public sealed record ProductFeedQuery(string Culture, string ImagesBaseUrl) : IQuery<ProductFeedFile>;

/// <param name="Uncategorised">The rows with no Google category up their branch (spec, *The degraded path*).</param>
public sealed record ProductFeedFile(byte[] Content, string ContentType, string FileName, int Rows, int Uncategorised);

public sealed class ProductFeedValidator : AbstractValidator<ProductFeedQuery>
{
    public ProductFeedValidator()
    {
        RuleFor(query => query.Culture).Must(culture => culture is "es" or "en").WithMessage("Supported cultures: es, en.");
    }
}

public sealed class ProductFeedEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /feeds/google/es.tsv
        //
        // Public (decision 5): Merchant Center fetches it on its own schedule, as a
        // crawler fetches a product page, and it says nothing a product page does not.
        // A query and not a command, so a daily fetch writes no audit row. Outside the
        // committed document, as /.well-known/ucp is: the document is the API the
        // front ends call, and this file is read by Merchant Center, in Google's shape.
        app.MapGet("/feeds/google/{culture}.tsv",
                async (string culture, HttpContext http, IQueryDispatcher queries, CancellationToken ct) =>
                {
                    var feed = await queries.SendAsync(new ProductFeedQuery(culture, $"{http.Request.Scheme}://{http.Request.Host}"), ct);
                    http.Response.Headers["X-Feed-Uncategorised"] = feed.Uncategorised.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return Results.File(feed.Content, feed.ContentType);
                })
            .AllowAnonymous()
            .WithName("GetGoogleProductFeed")
            .ExcludeFromDescription();
    }
}

/// <summary>
/// The product feed (phase 16, E-2; spec, *The product feed*): one row per variant on
/// sale, grouped by its product's code, in one culture. Its price is the catalogue's,
/// as the product page shows it — Merchant Center refuses a feed whose price differs
/// from its landing page; whether it can be bought now is the stock that can still be
/// sold; Google's category is the nearest one a person mapped up its branch. It reads
/// Postgres and the committed files and nothing else (spec, *The degraded path*): a
/// missing picture, barcode or category leaves its cell empty and the row in.
/// </summary>
public sealed class ProductFeedHandler(
    ICatalogueExport catalogue,
    IAvailabilityReader availability,
    GoogleCategoryMapping categories,
    IOptions<ExportsOptions> options,
    IExportFormats formats)
    : IQueryHandler<ProductFeedQuery, ProductFeedFile>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Exports);

    public async Task<ProductFeedFile> HandleAsync(ProductFeedQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("exports.product_feed");
        activity?.SetTag("export.culture", query.Culture);

        var onSale = (await catalogue.ProductsAsync(query.Culture, cancellationToken)).Where(product => product.OnSale).ToList();
        var skus = onSale.SelectMany(product => product.Variants).Select(variant => variant.Sku).ToList();
        var available = skus.Count == 0 ? new Dictionary<string, int>() : await availability.AvailableAsync(skus, cancellationToken);

        var items = onSale
            .SelectMany(product => product.Variants.Select(variant => Item(product, variant, query, available)))
            .ToList();

        var format = formats.Find<FeedItem>(GoogleMerchantFeed.Key)
            ?? throw new InvalidOperationException($"No {GoogleMerchantFeed.Key} format is registered for the feed.");
        using var output = new MemoryStream();
        await format.WriteAsync(items, new ExportWriting(query.Culture), output, cancellationToken);

        var uncategorised = items.Count(item => item.GoogleProductCategory is null);
        activity?.SetTag("export.rows", items.Count);
        activity?.SetTag("export.uncategorised", uncategorised);
        ExportMetrics.Record(ExportKind.Feed, format.Format, items.Count);

        return new ProductFeedFile(output.ToArray(), format.ContentType, $"google-{query.Culture}.{format.Extension}", items.Count, uncategorised);
    }

    private FeedItem Item(ExportedProduct product, ExportedVariant variant, ProductFeedQuery query, IReadOnlyDictionary<string, int> available)
    {
        // Its own gallery, or its product's when it has none.
        var pictures = variant.PictureIds.Count > 0 ? variant.PictureIds : [.. product.Pictures.Select(picture => picture.ImageId)];
        string Picture(string id) => $"{query.ImagesBaseUrl.TrimEnd('/')}/api/images/{id}";

        return new FeedItem(
            variant.Sku,
            product.Code,
            variant.Options.Count == 0 ? product.Name : $"{product.Name} - {string.Join(" / ", variant.Options.Select(option => option.Label))}",
            // Google requires a description; one the catalogue lacks is the product's name rather than nothing.
            product.Description ?? product.Name,
            $"{options.Value.StorefrontUrl.TrimEnd('/')}/{query.Culture}/p/{product.Slug}/{product.Code}",
            pictures.Count > 0 ? Picture(pictures[0]) : null,
            [.. pictures.Skip(1).Select(Picture)],
            available.GetValueOrDefault(variant.Sku) > 0 ? FeedAvailability.InStock : FeedAvailability.OutOfStock,
            variant.Price,
            product.Brand,
            variant.Barcode,
            categories.Of(product.Category),
            string.Join(" > ", product.Category.Select(step => step.Name)),
            variant.Options.FirstOrDefault(option => option.Axis == "COLOR")?.Label
                ?? product.Attributes.FirstOrDefault(attribute => attribute.Code == "COLOR")?.Value,
            variant.Options.FirstOrDefault(option => option.Axis == "SIZE")?.Label,
            product.Attributes.FirstOrDefault(attribute => attribute.Code == "GENDER")?.Key switch
            {
                "WOMEN" => FeedGender.Female,
                "MEN" => FeedGender.Male,
                "UNISEX" => FeedGender.Unisex,
                _ => null,
            });
    }
}
