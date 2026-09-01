using System.Diagnostics;
using Carter;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tendero.Catalog.Domain;
using Tendero.Catalog.Ports;
using Tendero.SharedKernel;

namespace Tendero.Catalog.Features.ListProducts;

/// <summary>
/// Lista el catálogo, filtrando por estado. Es lo que faltaba para que la cola
/// de revisión del backoffice fuese algo más que un estado vacío: la API tenía
/// cinco endpoints y ninguno permitía ver qué había en Draft, así que revisar un
/// catálogo importado pasaba por abrir Postgres.
///
/// Lee del catálogo, NO del índice, y eso es deliberado: lo que se revisa es
/// justamente lo que todavía no está indexado, porque sólo lo Active se indexa.
/// Una cola de revisión servida desde Elasticsearch estaría siempre vacía.
/// </summary>
public sealed record ListProductsQuery(string? Status, string Culture, int Page, int PageSize)
    : IQuery<ListProductsResult>;

public sealed record ProductSummary(
    string ProductId,
    string Name,
    string Slug,
    string? Brand,
    string? Category,
    decimal PriceAmount,
    string PriceCurrency,
    string? ImageId,
    string Status,
    IReadOnlyList<string> MissingCultures,
    DateTimeOffset UpdatedAt);

public sealed record ListProductsResult(
    IReadOnlyList<ProductSummary> Items, int Total, int Page, int PageSize);

public sealed class ListProductsValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsValidator()
    {
        // Un estado desconocido se rechaza en vez de ignorarse: filtrar por
        // "pendiente" y recibir el catalogo entero es peor que un 400, porque
        // parece que funciona.
        RuleFor(x => x.Status)
            .Must(status => status is null || Enum.TryParse<ProductStatus>(status, ignoreCase: true, out _))
            .WithMessage("Status must be one of: draft, active, archived.");

        RuleFor(x => x.Culture).Must(c => c is "es" or "en")
            .WithMessage("Supported cultures: es, en.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class ListProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/catalog/products?status=draft&culture=es&page=1&pageSize=20
        app.MapGet("/api/catalog/products",
            async (string? status, string? culture, int? page, int? pageSize,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                // Cultura: query param explícito > Accept-Language > "es".
                //
                // El parámetro gana porque una URL con idioma es compartible,
                // cacheable y es la ÚNICA vía para un agente UCP, que no tiene
                // locale de navegador. La cabecera decide cuando no hay
                // parámetro, que es lo que hace que alguien que llega sin pedir
                // nada vea su idioma y no el nuestro.
                //
                // Misma cadena que /api/search, copiada a propósito: son dos
                // casos, y extraer una abstracción con dos casos es adivinar.
                // Con un tercero se saca a un binder compartido.
                var resolved = culture
                    ?? http.Request.GetTypedHeaders().AcceptLanguage
                        .OrderByDescending(l => l.Quality ?? 1)
                        .Select(l => l.Value.Value?.Split('-')[0].ToLowerInvariant())
                        .FirstOrDefault(c => c is "es" or "en")
                    ?? "es";

                var result = await dispatcher.SendAsync(
                    new ListProductsQuery(status, resolved, page ?? 1, pageSize ?? 20), ct);

                // El cliente pide, el servidor declara. Vary porque la respuesta
                // depende de Accept-Language cuando no viene el parámetro.
                //
                // Declara la cultura NEGOCIADA, no la de cada fila: LocalizedText
                // cae en cadena, así que un producto sin inglés servido en "en"
                // vuelve en español. Una cabecera no puede decir "esta fila sí y
                // aquella no"; eso es lo que lleva `missingCultures` por ítem.
                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return Results.Ok(result);
            })
            .WithTags("Catalog")
            .WithName("ListProducts");
    }
}

public sealed class ListProductsHandler(IProductCatalogReader products)
    : IQueryHandler<ListProductsQuery, ListProductsResult>
{
    private static readonly ActivitySource Telemetry = new("Tendero.Catalog");
    private static readonly string[] Cultures = ["es", "en"];

    public async Task<ListProductsResult> HandleAsync(
        ListProductsQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.list");
        activity?.SetTag("catalog.status", query.Status);

        var status = query.Status is null
            ? (ProductStatus?)null
            : Enum.Parse<ProductStatus>(query.Status, ignoreCase: true);

        var page = await products.ListAsync(status, query.Page, query.PageSize, cancellationToken);

        activity?.SetTag("catalog.total", page.Total);

        return new ListProductsResult(
            [.. page.Items.Select(product => Summarise(product, query.Culture))],
            page.Total,
            query.Page,
            query.PageSize);
    }

    private static ProductSummary Summarise(Product product, string culture) =>
        new(product.Id.Value.ToString(),
            product.Name.In(culture),
            product.Slug.In(culture),
            product.Brand,
            product.Category,
            product.Price.Amount,
            product.Price.Currency,
            product.PrimaryImage?.Id.Value,
            product.Status.ToString().ToLowerInvariant(),
            // Qué idiomas le faltan al producto, resuelto aquí y no en el
            // cliente: es el dato por el que existe la cola. LocalizedText cae
            // en cadena (cultura → en → primera), así que sin esto la ficha se
            // ve completa en inglés y nadie se entera de que falta el español.
            [.. Cultures.Where(c => !product.Name.Cultures.Contains(c))],
            product.UpdatedAt);
}
