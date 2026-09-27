using System.Text.Json;
using ElGuerre.Tendero.Ordering.Features.CheckoutSessions;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests.Ucp;
using ElGuerre.Tendero.Ucp.Capabilities;
using ElGuerre.Tendero.Ucp.Checkout;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// Checkout's answers (P11-1), every state of one, against the specification's
/// own schemas: the checkout, and each negotiated extension's composition of it —
/// with negative controls, so a schema that accepts anything is caught rather than
/// quoted as evidence.
/// </summary>
public sealed class UcpCheckoutConformanceTests
{
    private const string Checkout = "https://ucp.dev/schemas/shopping/checkout.json";
    private const string WithFulfillment = "https://ucp.dev/schemas/shopping/fulfillment.json#/$defs/dev.ucp.shopping.checkout";
    private const string WithDiscount = "https://ucp.dev/schemas/shopping/discount.json#/$defs/dev.ucp.shopping.checkout";
    private const string ErrorResponse = "https://ucp.dev/schemas/common/types/error_response.json";

    internal static readonly UcpLinks Links = new("https://api.shop.example", "https://shop.example");

    private static readonly IReadOnlySet<string> All =
        new HashSet<string> { CheckoutCapability.Name, FulfillmentCapability.Name, DiscountCapability.Name };

    private static readonly Address Madrid = Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");

    internal static CheckoutSessionView Session(CheckoutState state, bool destination = true, bool option = true, bool email = true, string? orderId = null, bool canSettle = true, IReadOnlyList<CheckoutIssue>? issues = null) => new(
        Id: "0199a1f0-0000-7000-8000-00000000c0de",
        State: state,
        Mode: "test",
        Currency: "EUR",
        Culture: "es",
        Lines:
        [
            new CheckoutLineView("B073WXYZ01-38", "Zapatillas de running", "azul marino · 38", "img1", 2, 79.95m, 159.90m),
        ],
        // 159.90 − 16.00 + 4.95 = 148.85, with VAT inside it (ADR 0028): base 123.02 + 25.83.
        Totals: new CheckoutTotalsView(159.90m, 16.00m, option ? 4.95m : 0m, 25.83m, option ? 148.85m : 143.90m,
            [new CheckoutTaxView(0.21m, option ? 123.02m : 118.93m, option ? 25.83m : 24.97m)]),
        Buyer: new CheckoutBuyerDetails("Ana", "Ruiz", email ? "ana@example.com" : null, null),
        Destination: destination ? Madrid : null,
        ShippingOptions: destination ? [new CheckoutShippingOptionView("standard", "Envío estándar", 4.95m, 3)] : [],
        SelectedShippingOption: option && destination ? "standard" : null,
        Coupons: ["BIENVENIDA10", "NOPE"],
        Discounts: [new CheckoutDiscountView("BIENVENIDA10", "10 % de bienvenida", 16.00m)],
        Issues: issues ?? [],
        CanSettle: canSettle,
        OrderId: orderId,
        ExpiresAt: new DateTimeOffset(2026, 9, 27, 18, 0, 0, TimeSpan.Zero));

    private static JsonElement Json<T>(T answer) => JsonSerializer.SerializeToElement(answer);

    public static TheoryData<string, CheckoutSessionView> States => new()
    {
        { "incomplete, missing an email and a destination", Session(CheckoutState.Incomplete, destination: false, option: false, email: false,
            issues: [new(CheckoutIssueCodes.Missing, "buyer.email"), new(CheckoutIssueCodes.Missing, "fulfillment.destination"), new(CheckoutIssueCodes.DiscountCodeInvalid, "coupon:NOPE")]) },
        { "ready for complete", Session(CheckoutState.ReadyForComplete) },
        { "requires escalation", Session(CheckoutState.RequiresEscalation, canSettle: false, issues: [new(CheckoutIssueCodes.PaymentNotSettled, "payment")]) },
        { "completed", Session(CheckoutState.Completed, orderId: "0199a1f0-0000-7000-8000-0000000000aa") },
        { "canceled", Session(CheckoutState.Canceled) },
    };

    [Theory]
    [MemberData(nameof(States))]
    public void Every_state_validates_as_a_checkout_and_as_each_extension(string what, CheckoutSessionView session)
    {
        var wire = Json(UcpCheckoutMapper.ToWire(session, All, Links));

        UcpSchemas.Validate(Checkout, wire, $"A {what} checkout");
        UcpSchemas.Validate(WithFulfillment, wire, $"A {what} checkout with fulfillment");
        UcpSchemas.Validate(WithDiscount, wire, $"A {what} checkout with discounts");
    }

    /// <summary>
    /// The totals add up the way UCP tells a platform to add them: every entry but
    /// the total sums to the total. A VAT entry would break that, because prices
    /// include VAT (ADR 0028), so VAT is shown as lines inside the total — which
    /// sum to it.
    /// </summary>
    [Fact]
    public void The_totals_add_up_and_the_vat_is_inside_the_total_not_on_top_of_it()
    {
        var wire = UcpCheckoutMapper.ToWire(Session(CheckoutState.ReadyForComplete), All, Links);

        Assert.Equal(["subtotal", "items_discount", "fulfillment", "total"], wire.Totals.Select(total => total.Type));
        Assert.Equal(wire.Totals.Single(total => total.Type == "total").Amount, wire.Totals.Where(total => total.Type != "total").Sum(total => total.Amount));
        Assert.Equal(14885, wire.Totals.Single(total => total.Type == "total").Amount);
        Assert.DoesNotContain(wire.Totals, total => total.Type == "tax");
        Assert.Equal(14885, wire.Totals.Single(total => total.Type == "total").Lines!.Sum(line => line.Amount));
    }

    [Fact]
    public void Only_a_test_credential_is_offered_the_sandbox_handler_and_a_person_is_sent_to_continue()
    {
        var settled = UcpCheckoutMapper.ToWire(Session(CheckoutState.ReadyForComplete), All, Links);
        var escalated = UcpCheckoutMapper.ToWire(Session(CheckoutState.RequiresEscalation, canSettle: false, issues: [new(CheckoutIssueCodes.PaymentNotSettled, "payment")]), All, Links);

        Assert.Equal("sandbox", settled.Ucp.PaymentHandlers[UcpCheckoutMapper.SandboxHandlerName].Single().Config!["environment"]);
        Assert.Empty(escalated.Ucp.PaymentHandlers);
        Assert.Equal("https://shop.example/es/checkout/continue/0199a1f0-0000-7000-8000-00000000c0de", escalated.ContinueUrl);
        // requires_escalation MUST carry a message a person has to answer.
        Assert.Contains(escalated.Messages!, message => message.Severity == "requires_buyer_input");
    }

    /// <summary>
    /// The shop's own rules for agents (P11-6) reach the agent as a message it can
    /// act on: which limit, in the checkout's language, and a person to finish it.
    /// </summary>
    [Theory]
    [InlineData("es", "order_over_trust_cap: 120 EUR over the 100 cap for untrusted agents", "El total supera")]
    [InlineData("en", "orders_per_hour: 10 orders in the last hour, the most is 10", "This agent has placed")]
    [InlineData("en", "agent_suspended: claude-desktop is suspended", "This shop has suspended")]
    public void A_refusal_by_the_shops_rules_says_which_limit_in_the_checkouts_language(string culture, string detail, string starts)
    {
        var session = Session(CheckoutState.ReadyForComplete, issues: [new(CheckoutIssueCodes.AgentRefused, "payment", detail)]) with { Culture = culture };

        var wire = UcpCheckoutMapper.ToWire(session, All, Links);

        var message = Assert.Single(wire.Messages!);
        Assert.Equal((CheckoutIssueCodes.AgentRefused, "requires_buyer_input"), (message.Code, message.Severity));
        Assert.StartsWith(starts, message.Content, StringComparison.Ordinal);
        UcpSchemas.Validate(Checkout, Json(wire), "A checkout the shop's rules refused");
    }

    [Fact]
    public void A_completed_checkout_names_its_order_and_offers_no_way_to_continue()
    {
        var wire = UcpCheckoutMapper.ToWire(Session(CheckoutState.Completed, orderId: "0199a1f0-0000-7000-8000-0000000000aa"), All, Links);

        Assert.Equal(("completed", "https://shop.example/es/orders/0199a1f0-0000-7000-8000-0000000000aa"), (wire.Status, wire.Order!.PermalinkUrl));
        Assert.Null(wire.ContinueUrl);
        Assert.Null(wire.ExpiresAt);
    }

    [Fact]
    public void Issues_point_at_what_they_are_about_and_a_rejected_code_is_a_warning()
    {
        var wire = UcpCheckoutMapper.ToWire(Session(CheckoutState.Incomplete, destination: false, option: false, email: false,
            issues: [new(CheckoutIssueCodes.Missing, "buyer.email"), new(CheckoutIssueCodes.DiscountCodeInvalid, "coupon:NOPE"), new(CheckoutIssueCodes.ItemUnavailable, "line:B073WXYZ01-38")]), All, Links);

        Assert.Equal(
            [("error", "$.buyer.email", "recoverable"), ("warning", "$.discounts.codes[1]", null), ("error", "$.line_items[0]", "recoverable")],
            wire.Messages!.Select(message => (message.Type, message.Path, message.Severity)));
        Assert.All(wire.Messages!, message => Assert.False(string.IsNullOrWhiteSpace(message.Content)));
    }

    /// <summary>What was not negotiated is not in the answer (UCP §Response Capability Selection).</summary>
    [Fact]
    public void An_extension_the_platform_did_not_negotiate_is_absent()
    {
        var wire = UcpCheckoutMapper.ToWire(Session(CheckoutState.ReadyForComplete), new HashSet<string> { CheckoutCapability.Name }, Links);

        Assert.Null(wire.Fulfillment);
        Assert.Null(wire.Discounts);
        Assert.Equal([CheckoutCapability.Name], wire.Ucp.Capabilities.Keys);
        UcpSchemas.Validate(Checkout, Json(wire), "A checkout with no extensions");
    }

    /// <summary>Negative controls: the schemas bite, so the passes above mean something.</summary>
    [Fact]
    public void A_checkout_with_two_totals_or_a_positive_discount_does_not_validate()
    {
        var wire = UcpCheckoutMapper.ToWire(Session(CheckoutState.ReadyForComplete), All, Links);

        var twoTotals = Json(wire with { Totals = [.. wire.Totals, new UcpTotal { Type = "total", Amount = 1 }] });
        var positiveDiscount = Json(wire with { Totals = [.. wire.Totals.Select(total => total.Type == "items_discount" ? total with { Amount = 1600 } : total)] });

        Assert.False(UcpSchemas.Get(Checkout).Evaluate(twoTotals).IsValid);
        Assert.False(UcpSchemas.Get(Checkout).Evaluate(positiveDiscount).IsValid);
    }

    [Theory]
    [InlineData("not_found")]
    [InlineData("capabilities_incompatible")]
    public void A_refusal_with_no_checkout_is_an_error_response(string code)
    {
        var error = new UcpErrorResponse
        {
            Ucp = new UcpEnvelopeWithStatus { Version = UcpSpecification.Version },
            Messages = [new UcpCheckoutMessage { Type = "error", Code = code, Content = "x", Severity = "unrecoverable" }],
        };

        UcpSchemas.Validate(ErrorResponse, Json(error), $"A {code} error response");
    }

    // ---------- Requests ----------

    [Fact]
    public void A_request_becomes_lines_and_details_and_half_an_address_is_no_address()
    {
        var request = new UcpCheckoutRequest
        {
            LineItems = [new UcpLineItemRequest { Item = new UcpItemReference { Id = "B073WXYZ01-38" }, Quantity = 2 }],
            Buyer = new UcpBuyer { FirstName = "Ana", LastName = "Ruiz", Email = "ana@example.com" },
            Fulfillment = new UcpFulfillment
            {
                Methods =
                [
                    new UcpFulfillmentMethod
                    {
                        Type = "shipping",
                        Destinations = [new UcpDestination { StreetAddress = "Calle Mayor 1", AddressLocality = "Madrid", PostalCode = "28013", AddressCountry = "es" }],
                        Groups = [new UcpFulfillmentGroup { SelectedOptionId = "standard" }],
                    },
                ],
            },
            Discounts = new UcpDiscounts { Codes = ["BIENVENIDA10"] },
        };

        var details = UcpCheckoutMapper.Details(request);

        Assert.Equal([new CheckoutLineInput("B073WXYZ01-38", 2)], UcpCheckoutMapper.Lines(request));
        // The destination names nobody, so the buyer is who the parcel is for.
        Assert.Equal(("Ana Ruiz", "ES", "standard"), (details.Destination!.RecipientName, details.Destination.CountryCode, details.ShippingOptionCode));
        Assert.Equal(["BIENVENIDA10"], details.Coupons);

        var half = request with { Fulfillment = new UcpFulfillment { Methods = [new UcpFulfillmentMethod { Destinations = [new UcpDestination { StreetAddress = "Calle Mayor 1" }] }] } };
        Assert.Null(UcpCheckoutMapper.Details(half).Destination);
    }

    [Theory]
    [InlineData(39.00, 3900)]
    [InlineData(0.005, 1)]
    [InlineData(148.85, 14885)]
    public void Money_goes_out_in_minor_units(decimal amount, long minor) =>
        Assert.Equal(minor, UcpCheckoutMapper.Minor(amount));
}
