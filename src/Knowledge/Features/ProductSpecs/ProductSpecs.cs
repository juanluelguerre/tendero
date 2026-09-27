using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Knowledge.Features.ProductSpecs;

/// <summary>
/// The specs of one to four products, aligned row by row, each value with where
/// it came from (P10-7, P10-8). One product is the product page's table; several
/// are a comparison — the same rows, so the two can never disagree.
///
/// **Approved claims only, and "no data", never a guess** (ADR 0018). A value is
/// the catalogue's; an approved claim adds the sentence it rests on and how well
/// that sentence supports it, and only beside the value the claim put there. A
/// proposed claim appears nowhere. A product with no value for a row gets a cell
/// that says so — the constraint lives in this query, not in a prompt.
/// </summary>
public sealed record ProductSpecsQuery(IReadOnlyList<string> Codes, string Culture) : IQuery<SpecSheet>;

public sealed record SpecSheet(
    IReadOnlyList<SpecProduct> Products,
    IReadOnlyList<SpecRow> Rows,
    IReadOnlyList<SpecStatement> Statements);

public sealed record SpecProduct(string ProductId, string Code, string Name, string Slug);

/// <summary>One attribute across the products: one cell per product, in <see cref="SpecSheet.Products"/>' order.</summary>
public sealed record SpecRow(string AttributeCode, string Label, IReadOnlyList<SpecCell> Cells);

/// <summary>
/// One product's value for a row. <see cref="Source"/> is <c>catalogue</c> (the
/// catalogue holds it, from its import or a person), <c>description</c> (an
/// approved claim put it there, and <see cref="Evidence"/> says from which words)
/// or <c>none</c> — no data, and every value field null. The wording of each is
/// the interface's (invariant 6).
/// </summary>
public sealed record SpecCell(
    string Source, string? Kind, string? Value, decimal? Number, string? Unit, bool? Flag, SpecEvidence? Evidence);

public static class SpecSources
{
    public const string Catalogue = "catalogue";
    public const string Description = "description";
    public const string None = "none";
}

/// <summary>The words an approved claim rests on, and the weakest support among them.</summary>
public sealed record SpecEvidence(double Support, IReadOnlyList<SpecQuote> Quotes);

public sealed record SpecQuote(string Culture, string Text);

/// <summary>
/// An approved claim that is a sentence rather than a value — "it has an
/// anti-theft pocket" — or one the catalogue refused to hold as a value. It
/// belongs to one product and is not aligned: two sentences are not a row.
/// </summary>
public sealed record SpecStatement(string ProductId, string Text, SpecEvidence Evidence);

public sealed class ProductSpecsValidator : AbstractValidator<ProductSpecsQuery>
{
    public const int MaxProducts = 4;

    public ProductSpecsValidator()
    {
        RuleFor(query => query.Codes).NotEmpty()
            .Must(codes => codes.Count <= MaxProducts).WithMessage($"Compare at most {MaxProducts} products.");
        RuleForEach(query => query.Codes).NotEmpty().MaximumLength(40);
        RuleFor(query => query.Culture).Must(culture => culture is "es" or "en")
            .WithMessage("Supported cultures: es, en.");
    }
}

public sealed class ProductSpecsHandler(IProductFacts facts, IKnowledgeRepository knowledge)
    : IQueryHandler<ProductSpecsQuery, SpecSheet>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Knowledge);

    public async Task<SpecSheet> HandleAsync(ProductSpecsQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("knowledge.specs");
        activity?.SetTag("knowledge.products_requested", query.Codes.Count);
        activity?.SetTag("knowledge.culture", query.Culture);

        var products = await facts.OfAsync(query.Codes, query.Culture, cancellationToken);
        var sheets = new List<(ProductFacts Product, IReadOnlyList<ProductClaim> Approved)>(products.Count);
        foreach (var product in products)
        {
            var standing = await knowledge.StandingClaimsAsync(product.ProductId, cancellationToken);
            sheets.Add((product, [.. standing.Where(claim => claim.Status == ClaimStatus.Approved)]));
        }

        var quotes = new QuoteReader(knowledge, query.Culture);
        var rows = new List<SpecRow>();
        foreach (var (code, label) in RowsOf(products))
        {
            var cells = new List<SpecCell>(sheets.Count);
            foreach (var (product, approved) in sheets)
                cells.Add(await CellAsync(product, approved, code, quotes, cancellationToken));
            rows.Add(new SpecRow(code, label, cells));
        }

        var statements = new List<SpecStatement>();
        foreach (var (product, approved) in sheets)
        {
            // A sentence, or a value the catalogue would not hold (or has not been
            // asked to yet): true as a statement, not as a row.
            foreach (var claim in approved.Where(claim => claim.AttributeCode is null || claim.AppliedAs is null))
                statements.Add(new SpecStatement(
                    product.ProductId.ToString(), claim.Statement.In(query.Culture), await quotes.EvidenceAsync(claim, cancellationToken)));
        }

        activity?.SetTag("knowledge.products_found", products.Count);
        activity?.SetTag("knowledge.rows", rows.Count);
        activity?.SetTag("knowledge.statements", statements.Count);

        return new SpecSheet(
            [.. products.Select(product => new SpecProduct(product.ProductId.ToString(), product.Code, product.Name, product.Slug))],
            rows,
            statements);
    }

    /// <summary>Every attribute any of the products has, once, by label — the rows a comparison aligns on.</summary>
    private static IEnumerable<(string Code, string Label)> RowsOf(IReadOnlyList<ProductFacts> products) =>
        products
            .SelectMany(product => product.Attributes)
            .GroupBy(fact => fact.AttributeCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.First().AttributeCode, group.First().Label))
            .OrderBy(row => row.Label, StringComparer.OrdinalIgnoreCase);

    private static async Task<SpecCell> CellAsync(
        ProductFacts product, IReadOnlyList<ProductClaim> approved, string code, QuoteReader quotes, CancellationToken cancellationToken)
    {
        var fact = product.Attributes.FirstOrDefault(item => string.Equals(item.AttributeCode, code, StringComparison.OrdinalIgnoreCase));
        if (fact is null)
            return new SpecCell(SpecSources.None, null, null, null, null, null, null);

        // The quote stands beside the value only when the product holds exactly
        // what the claim put there: a reimport that changed it since leaves the
        // value with its catalogue source, and no sentence pretending to back it.
        var claim = approved
            .Where(item => string.Equals(item.AttributeCode, code, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(item.AppliedAs, fact.Key, StringComparison.OrdinalIgnoreCase))
            .MaxBy(item => item.Support);

        return new SpecCell(
            claim is null ? SpecSources.Catalogue : SpecSources.Description,
            fact.Kind, fact.Value, fact.Number, fact.Unit, fact.Flag,
            claim is null ? null : await quotes.EvidenceAsync(claim, cancellationToken));
    }

    /// <summary>
    /// A claim's quotes with their culture, which is their document's; each
    /// document read once. Only the page's language when the claim has words in
    /// it — the first live run showed "glass carafe" under "jarra de vidrio" on a
    /// Spanish page — and every quote when it has none, rather than no evidence.
    /// </summary>
    private sealed class QuoteReader(IKnowledgeRepository knowledge, string culture)
    {
        private readonly Dictionary<SourceDocumentId, string> cultures = [];

        public async Task<SpecEvidence> EvidenceAsync(ProductClaim claim, CancellationToken cancellationToken)
        {
            var quotes = new List<SpecQuote>(claim.Evidence.Count);
            foreach (var evidence in claim.Evidence)
            {
                if (!this.cultures.TryGetValue(evidence.Document, out var documentCulture))
                {
                    documentCulture = (await knowledge.FindDocumentAsync(evidence.Document, cancellationToken))?.Culture ?? "";
                    this.cultures[evidence.Document] = documentCulture;
                }

                quotes.Add(new SpecQuote(documentCulture, evidence.Quote.Text));
            }

            var inCulture = quotes.Where(quote => quote.Culture == culture).ToList();
            return new SpecEvidence(claim.Support, inCulture.Count > 0 ? inCulture : quotes);
        }
    }
}

public sealed class ProductSpecsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/knowledge/specs?codes=CAF-001,CAF-002&culture=es
        // Comma-separated rather than a repeated key: it is what an agent writes
        // first, and a tool can pass it through as it came.
        app.MapGet("/api/knowledge/specs",
            async Task<Results<Ok<SpecSheet>, NotFound>> (
                string codes, string? culture, HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                var sheet = await dispatcher.SendAsync(new ProductSpecsQuery(
                    [.. codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)], resolved), ct);

                // None of them is a product a shopper can see: the same 404 the
                // product page answers for a draft.
                return sheet.Products.Count == 0 ? TypedResults.NotFound() : TypedResults.Ok(sheet);
            })
            .AllowAnonymous()   // a product's specs are as public as its page
            .WithTags("Knowledge")
            .WithName("GetProductSpecs");
    }
}
