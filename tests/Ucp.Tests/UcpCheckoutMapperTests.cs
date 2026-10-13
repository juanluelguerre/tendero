using ElGuerre.Tendero.Ordering.Features.CheckoutSessions;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Ucp.Capabilities;
using ElGuerre.Tendero.Ucp.Checkout;
using ElGuerre.Tendero.Ucp.Negotiation;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// What the checkout mapper says and where it points (P11-1), found untested by
/// phase 11's Stryker close: the English half of every sentence, the JSONPath each
/// message names, the members that appear only when there is something to say, and
/// how a request's half-written address becomes no address.
/// </summary>
public sealed class UcpCheckoutMapperTests
{
    private static readonly IReadOnlySet<string> All =
        new HashSet<string> { DiscountCapability.Name, CheckoutCapability.Name, FulfillmentCapability.Name };

    private static CheckoutSessionView Ready(string culture = "es") =>
        UcpCheckoutConformanceTests.Session(CheckoutState.ReadyForComplete) with { Culture = culture };

    private static UcpCheckout Wire(CheckoutSessionView session, IReadOnlySet<string>? active = null) =>
        UcpCheckoutMapper.ToWire(session, active ?? All, UcpCheckoutConformanceTests.Links);

    [Fact]
    public void An_english_checkout_says_everything_in_english_and_a_spanish_one_in_spanish()
    {
        var en = Wire(Ready("en"));
        var es = Wire(Ready("es"));

        var total = en.Totals.Single(entry => entry.Type == "total");
        Assert.Equal("Total (VAT included)", total.DisplayText);
        Assert.Equal(["Base at 21% VAT", "VAT 21%"], total.Lines!.Select(line => line.DisplayText));
        Assert.Equal("Arrives in 3 days", en.Fulfillment!.Methods![0].Groups![0].Options![0].Description!.Plain);

        var totalEs = es.Totals.Single(entry => entry.Type == "total");
        Assert.Equal("Total (IVA incluido)", totalEs.DisplayText);
        Assert.Equal(["Base al 21 % de IVA", "IVA 21 %"], totalEs.Lines!.Select(line => line.DisplayText));
        Assert.Equal("Llega en 3 días", es.Fulfillment!.Methods![0].Groups![0].Options![0].Description!.Plain);
    }

    [Theory]
    [InlineData("en", CheckoutIssueCodes.Missing, "buyer.email", "The buyer's email is needed to confirm the order.")]
    [InlineData("es", CheckoutIssueCodes.Missing, "buyer.email", "Hace falta el email del comprador para confirmar el pedido.")]
    [InlineData("en", CheckoutIssueCodes.Missing, "fulfillment.destination", "A shipping address is needed.")]
    [InlineData("es", CheckoutIssueCodes.Missing, "fulfillment.destination", "Hace falta una dirección de envío.")]
    [InlineData("en", CheckoutIssueCodes.Missing, "fulfillment.option", "Choose a shipping option.")]
    [InlineData("en", CheckoutIssueCodes.ItemUnavailable, "line:B073WXYZ01-38", "This item cannot be bought right now.")]
    [InlineData("en", CheckoutIssueCodes.DiscountCodeInvalid, "coupon:NOPE", "This code does not apply to this checkout.")]
    [InlineData("en", CheckoutIssueCodes.PaymentNotSettled, "payment", "This shop does not take an agent's payment yet: a person completes the purchase at the continue URL.")]
    [InlineData("en", CheckoutIssueCodes.MandateScopeMismatch, "payment", "The mandate approves other terms than this checkout's now: ask the buyer to approve these.")]
    [InlineData("en", "mandate_required", "payment", "AP2 was negotiated: completing needs the buyer's checkout mandate.")]
    public void Each_message_is_written_in_the_checkouts_language(string culture, string code, string target, string content)
    {
        var wire = Wire(Ready(culture) with { Issues = [new CheckoutIssue(code, target)] });

        Assert.Equal(content, wire.Messages!.Single().Content);
    }

    [Theory]
    [InlineData("line:SECOND-SKU", true, "$.line_items[1]")]
    [InlineData("line:UNKNOWN", true, "$.line_items")]
    [InlineData("coupon:nope", true, "$.discounts.codes[1]")]
    [InlineData("coupon:OTHER", true, "$.discounts.codes")]
    [InlineData("fulfillment.destination", true, "$.fulfillment.methods[0].destinations")]
    [InlineData("fulfillment.destination", false, "$")]
    [InlineData("fulfillment.option", true, "$.fulfillment.methods[0].groups[0].selected_option_id")]
    [InlineData("fulfillment.option", false, "$")]
    [InlineData("payment", true, "$.payment")]
    [InlineData("totals", true, "$.totals")]
    public void A_message_points_at_what_it_is_about(string target, bool fulfillment, string path)
    {
        var session = Ready() with
        {
            Lines = [.. Ready().Lines, new CheckoutLineView("SECOND-SKU", "Otra cosa", null, null, 1, 10m, 10m)],
            Issues = [new CheckoutIssue(CheckoutIssueCodes.Missing, target)],
        };
        var active = fulfillment ? All : new HashSet<string> { CheckoutCapability.Name, DiscountCapability.Name };

        Assert.Equal(path, Wire(session, active).Messages!.Single().Path);
    }

    [Fact]
    public void Members_appear_only_when_there_is_something_to_say()
    {
        var ready = Wire(Ready());
        var nobody = Wire(Ready() with { Buyer = CheckoutBuyerDetails.Nobody, Destination = null, SelectedShippingOption = null, ShippingOptions = [] });

        Assert.NotNull(ready.ExpiresAt);
        Assert.Equal("ana@example.com", ready.Buyer!.Email);
        Assert.Equal(UcpCheckoutMapper.DestinationId, ready.Fulfillment!.Methods![0].SelectedDestinationId);
        Assert.Null(nobody.Buyer);
        Assert.Null(nobody.Fulfillment!.Methods![0].SelectedDestinationId);
        Assert.Null(Wire(Ready(), new HashSet<string> { CheckoutCapability.Name }).Fulfillment);
        Assert.Null(Wire(Ready(), new HashSet<string> { CheckoutCapability.Name }).Discounts);
    }

    [Fact]
    public void Vat_lines_are_shown_only_when_they_add_up_to_the_total()
    {
        var session = Ready();
        var off = session with { Totals = session.Totals! with { Taxes = [new CheckoutTaxView(0.21m, 100m, 21m)] } };

        Assert.NotNull(Wire(session).Totals.Single(entry => entry.Type == "total").Lines);
        Assert.Null(Wire(off).Totals.Single(entry => entry.Type == "total").Lines);
    }

    /// <summary>A code the agent sent is named; a discount the engine applied on its own is automatic and has none.</summary>
    [Fact]
    public void A_discount_says_whether_the_agent_asked_for_it()
    {
        var session = Ready() with
        {
            Discounts = [new CheckoutDiscountView("BIENVENIDA10", "10 % de bienvenida", 16m), new CheckoutDiscountView("AUTO-2X1", "2x1", 5m)],
        };

        var applied = Wire(session).Discounts!.Applied!;

        Assert.Equal(("BIENVENIDA10", false), (applied[0].Code, applied[0].Automatic));
        Assert.Equal(((string?)null, true), (applied[1].Code, applied[1].Automatic));
    }

    [Fact]
    public void The_capabilities_are_listed_in_a_stable_order() =>
        Assert.Equal(
            [CheckoutCapability.Name, DiscountCapability.Name, FulfillmentCapability.Name],
            Wire(Ready()).Ucp.Capabilities.Keys);

    [Theory]
    [InlineData(null, "Calle Mayor 1", "Madrid", "28013", "ES", "Ana Ruiz")]
    [InlineData("Beatriz", "Calle Mayor 1", "Madrid", "28013", "es", "Beatriz")]
    public void A_whole_address_is_taken_and_its_recipient_is_the_buyer_when_it_names_nobody(
        string? firstName, string street, string locality, string postal, string country, string recipient)
    {
        var details = UcpCheckoutMapper.Details(Request(new UcpDestination
        {
            Id = "d",
            FirstName = firstName,
            StreetAddress = street,
            AddressLocality = locality,
            PostalCode = postal,
            AddressCountry = country,
        }));

        Assert.Equal((recipient, "ES"), (details.Destination!.RecipientName, details.Destination.CountryCode));
    }

    [Theory]
    [InlineData("", "Madrid", "28013", "ES")]
    [InlineData("Calle Mayor 1", "", "28013", "ES")]
    [InlineData("Calle Mayor 1", "Madrid", "", "ES")]
    [InlineData("Calle Mayor 1", "Madrid", "28013", "ESP")]
    public void Any_part_missing_is_no_address(string street, string locality, string postal, string country) =>
        Assert.Null(UcpCheckoutMapper.Details(Request(new UcpDestination
        {
            Id = "d",
            StreetAddress = street,
            AddressLocality = locality,
            PostalCode = postal,
            AddressCountry = country,
        })).Destination);

    [Fact]
    public void Without_a_buyer_or_a_name_on_the_address_nobody_can_receive_it()
    {
        var details = UcpCheckoutMapper.Details(new UcpCheckoutRequest
        {
            Fulfillment = new UcpFulfillment
            {
                Methods = [new UcpFulfillmentMethod { Id = "shipping", Type = "shipping", Destinations = [new UcpDestination { Id = "d", StreetAddress = "Calle Mayor 1", AddressLocality = "Madrid", PostalCode = "28013", AddressCountry = "ES" }] }],
            },
        });

        Assert.Equal((CheckoutBuyerDetails.Nobody, (Address?)null), (details.Buyer, details.Destination));
        Assert.Empty(details.Coupons);
        Assert.Empty(UcpCheckoutMapper.Lines(new UcpCheckoutRequest()));
    }

    private static UcpCheckoutRequest Request(UcpDestination destination) => new()
    {
        Buyer = new UcpBuyer { FirstName = "Ana", LastName = "Ruiz", Email = "ana@example.com" },
        Fulfillment = new UcpFulfillment { Methods = [new UcpFulfillmentMethod { Id = "shipping", Type = "shipping", Destinations = [destination] }] },
    };

    [Fact]
    public async Task Completion_takes_one_instrument_from_the_offered_handler_with_a_token_and_nothing_else()
    {
        var operations = new UcpCheckoutOperations(null!, null!, null!, null!, null!, null!);
        var call = new UcpCheckoutCall(new UcpNegotiated(All, new PlatformProfile(new Dictionary<string, IReadOnlySet<string>>())), UcpCheckoutConformanceTests.Links);
        var ct = TestContext.Current.CancellationToken;
        UcpCompleteRequest With(string? handler, string? token) => new()
        {
            Payment = new UcpPaymentRequest { Instruments = [new UcpInstrumentRequest { HandlerId = handler, Credential = new UcpCredentialRequest { Token = token } }] },
        };

        var otherHandler = await operations.CompleteAsync(Guid.NewGuid(), With("someone_else", "tok"), "k", "es", call, ct);
        var noToken = await operations.CompleteAsync(Guid.NewGuid(), With(UcpCheckoutMapper.SandboxHandlerId, " "), "k", "es", call, ct);
        var noPayment = await operations.CompleteAsync(Guid.NewGuid(), new UcpCompleteRequest(), "k", "es", call, ct);

        Assert.All([otherHandler, noToken, noPayment], answer => Assert.Equal((400, "invalid_request"), (answer.Status, ((UcpProtocolError)answer.Body).Code)));
    }
}
