using System.Diagnostics;
using System.Globalization;
using Carter;
using ElGuerre.Tendero.Pricing.Adapters;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Pricing.Features.QuoteCart;

/// <summary>
/// Quotes a cart: prices by segment, promotions with their combination rules,
/// tax and total.
///
/// **It is a Pricing slice and it works without a cart existing.** That is the
/// point of Pricing being its own context: the storefront has to show
/// tax-inclusive, discounted prices long before there is a <c>Cart</c>
/// aggregate, and an agent over UCP will want to know what something costs
/// before deciding whether to add it.
///
/// The request carries SKU and quantity and nothing else. Everything else — list
/// price, category branch, tax class, segment — is supplied by the server: a
/// body that declared its own category would claim the KITCHEN discount on a
/// backpack.
/// </summary>
public sealed record QuoteCartQuery(
    string Culture,
    string? Segment,
    IReadOnlyList<QuoteLineInput> Lines,
    decimal? Shipping,
    IReadOnlyList<string> Coupons) : IQuery<QuoteCartResult>;

public sealed record QuoteLineInput(string Sku, int Quantity);

public enum QuoteOutcome
{
    Quoted,

    /// <summary>Some SKU does not exist. Which one is named rather than pricing
    /// the rest: a total with a line missing is worse than an error.</summary>
    UnknownSkus,

    /// <summary>Two currencies in one cart. Multi-currency is deferred on
    /// purpose, and deferred means checked, not ignored.</summary>
    MixedCurrencies
}

public sealed record QuoteCartResult(
    QuoteOutcome Outcome,
    PriceQuote? Quote,
    IReadOnlyList<string> Offending);

public sealed class QuoteCartValidator : AbstractValidator<QuoteCartQuery>
{
    private const int MaximumLines = 50;

    public QuoteCartValidator()
    {
        RuleFor(query => query.Lines)
            .NotEmpty().WithMessage("A quote needs at least one line.")
            .Must(lines => lines.Count <= MaximumLines)
                .WithMessage($"A quote takes at most {MaximumLines} lines.")
            .Must(lines => lines.All(line => !string.IsNullOrWhiteSpace(line.Sku)))
                .WithMessage("Every line needs a SKU.")
            .Must(lines => lines.All(line => line.Quantity > 0))
                .WithMessage("Quantities must be positive.")
            // A repeated SKU is not fixed by quietly adding the quantities: the
            // caller and the response would stop having the same lines, and the
            // quote's fingerprint would never match again at checkout.
            .Must(lines => lines.Select(line => line.Sku.Trim())
                                .Distinct(StringComparer.OrdinalIgnoreCase).Count() == lines.Count)
                .WithMessage("A SKU may appear only once; use its quantity.");

        RuleFor(query => query.Shipping)
            .GreaterThanOrEqualTo(0m).When(query => query.Shipping is not null)
            .WithMessage("Shipping cannot be negative.");
    }
}

// ---------- The shape that travels over the wire ----------

public sealed record QuoteCartRequest(
    IReadOnlyList<QuoteLineRequest> Lines,
    string? Segment = null,
    decimal? Shipping = null,
    IReadOnlyList<string>? Coupons = null);

public sealed record QuoteLineRequest(string Sku, int Quantity);

/// <summary>
/// Ids travel as flat strings. <c>VariantId</c> is a record struct and
/// serialises as <c>{"value":"…"}</c>: returning it raw is the mistake CLAUDE.md
/// warns that no unit test will catch.
/// </summary>
public sealed record QuotedLineResponse(
    string VariantId,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    string PriceSource,
    decimal Discount,
    decimal Net);

public sealed record AppliedDiscountResponse(
    string PromotionCode,
    string Label,
    decimal Amount,
    string Effect,
    string Combination,
    string Outcome,
    string? ReasonCode,
    string? Reason);

public sealed record TaxLineResponse(string TaxClass, decimal Rate, decimal Base, decimal Amount);

public sealed record QuoteResponse(
    string QuoteId,
    string InputHash,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string Currency,
    string Segment,
    IReadOnlyList<QuotedLineResponse> Lines,
    IReadOnlyList<AppliedDiscountResponse> Discounts,
    IReadOnlyList<TaxLineResponse> Taxes,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal Shipping,
    decimal TaxTotal,
    decimal Total);

public sealed class QuoteCartEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/pricing/quote",
            async Task<Results<Ok<QuoteResponse>, BadRequest<string>>> (
                   QuoteCartRequest request, string? culture,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(
                    new QuoteCartQuery(
                        resolved,
                        request.Segment,
                        [.. request.Lines.Select(line => new QuoteLineInput(line.Sku, line.Quantity))],
                        request.Shipping,
                        request.Coupons ?? []),
                    ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return result.Outcome switch
                {
                    QuoteOutcome.UnknownSkus => TypedResults.BadRequest(
                        $"Unknown SKUs: {string.Join(", ", result.Offending)}."),
                    QuoteOutcome.MixedCurrencies => TypedResults.BadRequest(
                        $"A cart carries one currency; this one carries {string.Join(", ", result.Offending)}."),
                    _ => TypedResults.Ok(Response(result.Quote!, resolved))
                };
            })
            // Public, and said in code: a guest has to see prices, and an agent
            // asks what something costs before identifying itself. What is NOT
            // public is the tariff: the segment comes from the principal, never
            // from the body (see Segments).
            .AllowAnonymous()
            .WithTags("Pricing")
            .WithName("QuoteCart");
    }

    private static QuoteResponse Response(PriceQuote quote, string culture) => new(
        quote.QuoteId,
        quote.InputHash,
        quote.IssuedAt,
        quote.ExpiresAt,
        quote.Currency,
        quote.Segment,
        [.. quote.Lines.Select(line => new QuotedLineResponse(
            line.VariantId.ToString(), line.Sku, line.Quantity,
            line.UnitPrice.Amount, line.PriceSource, line.Discount.Amount, line.Net.Amount))],
        [.. quote.Discounts.Select(discount => new AppliedDiscountResponse(
            discount.PromotionCode,
            discount.Label.In(culture),
            discount.Amount.Amount,
            discount.EffectKind,
            discount.Combination.ToString(),
            discount.Outcome.ToString(),
            discount.Reason?.Code,
            discount.Reason?.Explanation.In(culture)))],
        [.. quote.Taxes.Select(tax => new TaxLineResponse(
            tax.TaxClass, tax.Rate, tax.Base.Amount, tax.Amount.Amount))],
        quote.Subtotal.Amount,
        quote.DiscountTotal.Amount,
        quote.Shipping.Amount,
        quote.TaxTotal.Amount,
        quote.Total.Amount);
}

public sealed class QuoteCartHandler(
    IPricedItemReader items,
    IPriceListReader priceLists,
    IPromotionReader promotions,
    IPriceResolver resolver,
    IPromotionEngine engine,
    ITaxCalculatorRegistry taxes,
    IPrincipalAccessor principal,
    IOptions<PricingSeedOptions> options,
    TimeProvider clock)
    : IQueryHandler<QuoteCartQuery, QuoteCartResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Pricing);

    public async Task<QuoteCartResult> HandleAsync(
        QuoteCartQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("pricing.quote");
        activity?.SetTag("pricing.line_count", query.Lines.Count);

        var settings = options.Value;
        var segment = Segments.For(principal.Current, query.Segment, settings.DefaultSegment);
        var at = clock.GetUtcNow();

        activity?.SetTag("pricing.segment", segment);

        var skus = query.Lines.Select(line => line.Sku.Trim()).ToArray();
        var known = await items.BySkusAsync(skus, cancellationToken);

        var bySku = known.ToDictionary(item => item.Sku, StringComparer.OrdinalIgnoreCase);
        var unknown = skus.Where(sku => !bySku.ContainsKey(sku)).ToArray();
        if (unknown.Length > 0)
        {
            activity?.SetTag("pricing.unknown_skus", unknown.Length);
            return new QuoteCartResult(QuoteOutcome.UnknownSkus, null, unknown);
        }

        var currencies = known
            .Select(item => item.CatalogPrice.Currency.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (currencies.Length > 1)
            return new QuoteCartResult(QuoteOutcome.MixedCurrencies, null, currencies);

        var currency = currencies[0];
        var book = await priceLists.BookAsync(cancellationToken);

        var lines = query.Lines
            .Select(line => Price(bySku[line.Sku.Trim()], line.Quantity, book, segment, at))
            .ToArray();

        var shipping = new Money(query.Shipping ?? 0m, currency);
        var cart = new PricedCart(currency, segment, lines, shipping);

        var all = await promotions.AllAsync(cancellationToken);
        var outcome = engine.Apply(all, cart, new PromotionContext(at, query.Coupons));

        activity?.SetTag("pricing.promotions_applied",
            outcome.Discounts.Count(discount => discount.Outcome == DiscountOutcome.Applied));
        activity?.SetTag("pricing.promotions_suppressed",
            outcome.Discounts.Count(discount => discount.Outcome == DiscountOutcome.Suppressed));

        return new QuoteCartResult(
            QuoteOutcome.Quoted,
            Assemble(outcome, book, all, query, segment, at, settings),
            []);
    }

    private PricedLine Price(
        PricedItem item, int quantity, PriceBook book, string segment, DateTimeOffset at)
    {
        var price = resolver.Resolve(book, new PriceRequest(item.Sku, item.CatalogPrice, segment, at));

        return new PricedLine(
            item.VariantId, item.Sku, quantity, price.Unit, price.Source,
            item.CategoryPath, item.TaxClass, Money.Zero(item.CatalogPrice.Currency));
    }

    private PriceQuote Assemble(
        PromotionOutcome outcome,
        PriceBook book,
        IReadOnlyList<Promotion> promotionSet,
        QuoteCartQuery query,
        string segment,
        DateTimeOffset at,
        PricingSeedOptions settings)
    {
        var cart = outcome.Cart;
        var currency = cart.Currency;

        // Shipping is taxed. It goes in as one more base, at the standard
        // class: carriage with its own rate exists in some jurisdictions and
        // would be another adapter, not an `if` here.
        var calculator = taxes.Get(settings.TaxCalculator);
        var assessment = calculator.Assess(new TaxRequest(
            currency,
            [
                .. cart.Lines.Select(line =>
                    new TaxableAmount(line.TaxClass ?? TaxableAmount.Standard, line.Net)),
                new TaxableAmount(TaxableAmount.Standard, cart.Shipping)
            ]));

        var total = cart.Net + cart.Shipping + assessment.Total;

        var inputHash = Fingerprint(query, segment, book, promotionSet);

        return new PriceQuote(
            QuoteId: Guid.CreateVersion7().ToString(),
            InputHash: inputHash,
            IssuedAt: at,
            ExpiresAt: at + PriceQuote.Lifetime,
            Currency: currency,
            Segment: segment,
            Lines: [.. cart.Lines.Select(line => new QuotedLine(
                line.VariantId, line.Sku, line.Quantity, line.UnitPrice,
                line.PriceSource, line.Discount, line.Net))],
            Discounts: outcome.Discounts,
            Taxes: [.. assessment.Lines.Select(tax =>
                new TaxLineView(tax.TaxClass, tax.Rate, tax.Base, tax.Amount))],
            Subtotal: cart.Gross,
            DiscountTotal: cart.DiscountTotal,
            Shipping: cart.Shipping,
            TaxTotal: assessment.Total,
            Total: total);
    }

    /// <summary>
    /// The fingerprint covers what was asked AND the data it was answered with.
    ///
    /// The second half is the one that gets forgotten: without it, dropping a
    /// price in the backoffice would leave live quotes promising the old one,
    /// and checkout revalidation would have no way to notice.
    /// </summary>
    private static string Fingerprint(
        QuoteCartQuery query, string segment, PriceBook book, IReadOnlyList<Promotion> promotions) =>
        Fingerprints.Of(
        [
            segment,
            string.Join(',', query.Lines
                .OrderBy(line => line.Sku, StringComparer.OrdinalIgnoreCase)
                .Select(line => $"{line.Sku.Trim().ToUpperInvariant()}x{line.Quantity}")),
            string.Join(',', query.Coupons.Order(StringComparer.OrdinalIgnoreCase)),
            (query.Shipping ?? 0m).ToString(CultureInfo.InvariantCulture),
            book.Fingerprint(),
            Fingerprints.Of(promotions
                .OrderBy(promotion => promotion.Code, StringComparer.Ordinal)
                .Select(promotion => promotion.Canonical()))
        ]);
}
