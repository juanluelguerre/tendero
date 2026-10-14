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
    IReadOnlyList<QuoteLineInput> Lines,
    Money? Shipping,
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
            .Must(shipping => shipping!.Value.Amount >= 0m).When(query => query.Shipping is not null)
            .WithMessage("Shipping cannot be negative.");
    }
}

// ---------- The shape that travels over the wire ----------

public sealed record QuoteCartRequest(
    IReadOnlyList<QuoteLineRequest> Lines,
    Money? Shipping = null,
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
    Money UnitPrice,
    string PriceSource,
    /// <summary>What came off this line: an amount, never negative — the name says which way it goes.</summary>
    Money Discount,
    Money Net);

public sealed record AppliedDiscountResponse(
    string PromotionCode,
    string Label,
    /// <summary>What it took off, never negative: zero for one that did not apply, with its reason beside it.</summary>
    Money Amount,
    string Effect,
    CombinationPolicy Combination,
    DiscountOutcome Outcome,
    /// <summary>Why it did not apply, to branch on; null for one that did.</summary>
    RuleReasonCode? ReasonCode,
    /// <summary>The same, as a sentence in the language asked: what an agent reads. A page composes its own from the code and what follows.</summary>
    string? Reason,
    /// <summary>What it asked for and did not get, for <see cref="RuleReasonCode.ConditionNotMet"/>.</summary>
    DiscountRequirement? Requires,
    /// <summary>The promotion that took its place, by name, when one did.</summary>
    string? Winner);

/// <summary>
/// A promotion's condition as a page needs it (ADR 0059): the money as money,
/// the category and the article by their codes — a page has the names — and how
/// far the cart is from it, worked out by the shop.
/// </summary>
public sealed record DiscountRequirement(
    Money? MinimumSubtotal,
    int? MinimumQuantity,
    string? CategoryCode,
    string? Sku,
    /// <summary>How much more the cart has to be worth, when the minimum is what failed.</summary>
    Money? ShortBy,
    /// <summary>How many more units of what it reaches, when the quantity is what failed.</summary>
    int? UnitsShort);

public sealed record TaxLineResponse(string TaxClass, decimal Rate, Money Base, Money Amount);

public sealed record QuoteResponse(
    string QuoteId,
    string InputHash,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string Segment,
    IReadOnlyList<QuotedLineResponse> Lines,
    IReadOnlyList<AppliedDiscountResponse> Discounts,
    IReadOnlyList<TaxLineResponse> Taxes,
    Money Subtotal,
    /// <summary>Every discount that applied, added up: an amount, never negative. The total is the subtotal less this, plus shipping.</summary>
    Money DiscountTotal,
    Money Shipping,
    Money TaxTotal,
    Money Total);

/// <summary>What a quote refuses, by code.</summary>
public static class QuoteRefusals
{
    public static readonly Refusal UnknownSkus = new(
        "UnknownSkus", 400, "Hay artículos que la tienda no vende.", "Some of these are not on sale here.");

    public static readonly Refusal MixedCurrencies = new(
        "MixedCurrencies", 400, "Un carrito lleva una sola moneda.", "A cart carries one currency.");
}

public sealed class QuoteCartEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/pricing/quote",
            async Task<Results<Ok<QuoteResponse>, ProblemHttpResult>> (
                   QuoteCartRequest request, string? culture,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(
                    new QuoteCartQuery(
                        resolved,
                        [.. request.Lines.Select(line => new QuoteLineInput(line.Sku, line.Quantity))],
                        request.Shipping,
                        request.Coupons ?? []),
                    ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return result.Outcome switch
                {
                    QuoteOutcome.UnknownSkus => TypedResults.Problem(
                        statusCode: QuoteRefusals.UnknownSkus.Status,
                        extensions: QuoteRefusals.UnknownSkus.Said(
                            es: $"No se venden: {string.Join(", ", result.Offending)}.",
                            en: $"Not on sale: {string.Join(", ", result.Offending)}.",
                            path: "$.lines")),
                    QuoteOutcome.MixedCurrencies => TypedResults.Problem(
                        statusCode: QuoteRefusals.MixedCurrencies.Status,
                        extensions: QuoteRefusals.MixedCurrencies.Said(
                            es: $"Este llevaría {string.Join(", ", result.Offending)}.",
                            en: $"This one would carry {string.Join(", ", result.Offending)}.")),
                    _ => TypedResults.Ok(Response(result.Quote!, resolved))
                };
            })
            // Public, and said in code: a guest has to see prices, and an agent
            // asks what something costs before identifying itself. What is NOT
            // public is the tariff: the segment comes from the principal, never
            // from the body (see Segments).
            .AllowAnonymous()
            .WithMetadata(new Refuses(QuoteRefusals.UnknownSkus, QuoteRefusals.MixedCurrencies))
            .WithTags("Pricing")
            .WithName("QuoteCart");
    }

    private static QuoteResponse Response(PriceQuote quote, string culture) => new(
        quote.QuoteId,
        quote.InputHash,
        quote.IssuedAt,
        quote.ExpiresAt,
        quote.Segment,
        [.. quote.Lines.Select(line => new QuotedLineResponse(
            line.VariantId.ToString(), line.Sku, line.Quantity,
            line.UnitPrice, line.PriceSource, line.Discount, line.Net))],
        [.. quote.Discounts.Select(discount => new AppliedDiscountResponse(
            discount.PromotionCode,
            discount.Label.In(culture),
            discount.Amount,
            discount.EffectKind,
            discount.Combination,
            discount.Outcome,
            discount.Reason?.Code,
            discount.Reason?.Explanation.In(culture),
            discount.Reason is { Requires: { } asked } reason
                ? new DiscountRequirement(
                    asked.MinimumSubtotal, asked.MinimumQuantity, asked.CategoryCode, asked.Sku,
                    reason.ShortBy, reason.UnitsShort)
                : null,
            discount.Reason?.Winner?.In(culture)))],
        [.. quote.Taxes.Select(tax => new TaxLineResponse(
            tax.TaxClass, tax.Rate, tax.Base, tax.Amount))],
        quote.Subtotal,
        quote.DiscountTotal,
        quote.Shipping,
        quote.TaxTotal,
        quote.Total);
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
        var segment = Segments.For(principal.Current, settings.DefaultSegment);
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

        // Shipping in another currency than the goods' is the same mistake as
        // goods in two: one cart, one currency.
        if (query.Shipping is { } sent && !string.Equals(sent.Currency, currency, StringComparison.OrdinalIgnoreCase))
            return new QuoteCartResult(QuoteOutcome.MixedCurrencies, null, [.. new[] { currency, sent.Currency.ToUpperInvariant() }.Order(StringComparer.Ordinal)]);

        var book = await priceLists.BookAsync(cancellationToken);

        var lines = query.Lines
            .Select(line => Price(bySku[line.Sku.Trim()], line.Quantity, book, segment, at))
            .ToArray();

        var shipping = new Money(query.Shipping?.Amount ?? 0m, currency);
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

        // Shipping carries VAT too. It goes in as one more amount, at the
        // standard class: carriage with its own rate exists in some
        // jurisdictions and would be another adapter, not an `if` here.
        var calculator = taxes.Get(settings.TaxCalculator);
        var assessment = calculator.Assess(new TaxRequest(
            currency,
            [
                .. cart.Lines.Select(line =>
                    new TaxableAmount(line.TaxClass ?? TaxableAmount.Standard, line.Net)),
                new TaxableAmount(TaxableAmount.Standard, cart.Shipping)
            ]));

        // Prices include VAT (ADR 0028), so the total is what the lines and the
        // shipping add up to — the figure the shopper can check against the
        // lines they were shown — and the assessment says how much of it is
        // tax. Adding the assessment here is the defect this line replaced.
        var total = cart.Net + cart.Shipping;

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
            (query.Shipping?.Amount ?? 0m).ToString(CultureInfo.InvariantCulture),
            book.Fingerprint(),
            Fingerprints.Of(promotions
                .OrderBy(promotion => promotion.Code, StringComparer.Ordinal)
                .Select(promotion => promotion.Canonical()))
        ]);
}
