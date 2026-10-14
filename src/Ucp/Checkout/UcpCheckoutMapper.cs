using System.Globalization;
using ElGuerre.Tendero.Ordering.Features.CheckoutSessions;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Ucp.Capabilities;
using ElGuerre.Tendero.Ucp.Catalog;

namespace ElGuerre.Tendero.Ucp.Checkout;

/// <summary>Where the shop's own pages are: an order's permalink and the page a person continues on.</summary>
public sealed record UcpLinks(string ApiBaseUrl, string StorefrontUrl)
{
    public string OrderPage(string culture, string orderId) => $"{StorefrontUrl.TrimEnd('/')}/{culture}/orders/{orderId}";

    /// <summary>A person picks the session up on the storefront and pays there (requires_escalation).</summary>
    public string Continue(string culture, string sessionId) => $"{StorefrontUrl.TrimEnd('/')}/{culture}/checkout/continue/{sessionId}";

    public string RefundPolicy => $"{ApiBaseUrl.TrimEnd('/')}/api/returns/policy";

    /// <summary>The same image URL the catalogue binding answers with: one rule, one place.</summary>
    public string Image(string imageId) => UcpCatalogMapper.ImageUrl(imageId, ApiBaseUrl);
}

/// <summary>
/// Ordering's checkout session as UCP's checkout (P11-1), and UCP's requests as
/// Ordering's commands. Pure: no dispatcher, no HTTP — which is what lets the
/// conformance tests validate every shape against the specification's schemas.
/// </summary>
public static class UcpCheckoutMapper
{
    /// <summary>The one method Tendero offers, and its one destination and group — named, because updates refer to them.</summary>
    public const string ShippingMethodId = "shipping";

    public const string DestinationId = "dest_1";

    public const string GroupId = "package_1";

    /// <summary>The sandbox handler: the fake provider, offered to a test credential only (ADR 0038 §6).</summary>
    public const string SandboxHandlerName = "com.elguerre.tendero.fake";

    public const string SandboxHandlerId = "tendero_fake_1";

    public const string SandboxInstrumentType = "card";

    public static IReadOnlyDictionary<string, IReadOnlyList<UcpPaymentHandler>> Handlers(bool canSettle) =>
        canSettle
            ? new Dictionary<string, IReadOnlyList<UcpPaymentHandler>> { [SandboxHandlerName] = [SandboxHandler] }
            : new Dictionary<string, IReadOnlyList<UcpPaymentHandler>>();

    public static readonly UcpPaymentHandler SandboxHandler = new()
    {
        Id = SandboxHandlerId,
        Version = UcpSpecification.Version,
        AvailableInstruments = [new UcpAvailableInstrument { Type = SandboxInstrumentType }],
        // UCP's own place for whether money moves (ADR 0038): not a flag on the
        // order, a property of how it is paid.
        Config = new Dictionary<string, string> { ["environment"] = "sandbox" },
    };

    public static UcpCheckout ToWire(CheckoutSessionView session, IReadOnlySet<string> active, UcpLinks links)
    {
        var lineIds = session.Lines.Select(line => line.Sku).ToList();
        var fulfillment = active.Contains(FulfillmentCapability.Name);
        var discounts = active.Contains(DiscountCapability.Name);

        return new UcpCheckout
        {
            Ucp = new UcpCheckoutEnvelope
            {
                Version = UcpSpecification.Version,
                Capabilities = active
                    .Order(StringComparer.Ordinal)
                    .ToDictionary(name => name, IReadOnlyList<UcpVersionOnly> (_) => [new UcpVersionOnly { Version = UcpSpecification.Version }]),
                PaymentHandlers = Handlers(session.CanSettle),
            },
            Id = session.Id,
            Status = Status(session.State),
            Currency = session.Currency,
            LineItems = [.. session.Lines.Select(line => new UcpLineItem
            {
                Id = line.Sku,
                Item = new UcpItem
                {
                    Id = line.Sku,
                    Title = line.VariantLabel is { Length: > 0 } label ? $"{line.Title} · {label}" : line.Title,
                    Price = Minor(line.UnitPrice, session.Currency),
                    ImageUrl = line.ImageId is { } image ? links.Image(image) : null,
                },
                Quantity = line.Quantity,
                Totals =
                [
                    new UcpTotal { Type = "subtotal", Amount = Minor(line.Total, session.Currency) },
                    new UcpTotal { Type = "total", Amount = Minor(line.Total, session.Currency) },
                ],
            })],
            Totals = Totals(session),
            Links = [new UcpLink { Type = "refund_policy", Url = links.RefundPolicy }],
            Buyer = Buyer(session.Buyer),
            Messages = Messages(session, lineIds, fulfillment) is { Count: > 0 } messages ? messages : null,
            ContinueUrl = session.State is CheckoutState.Completed or CheckoutState.Canceled ? null : links.Continue(session.Culture, session.Id),
            ExpiresAt = session.State is CheckoutState.Completed or CheckoutState.Canceled
                ? null
                : session.ExpiresAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            Fulfillment = fulfillment ? Fulfillment(session, lineIds) : null,
            Discounts = discounts
                ? new UcpDiscounts
                {
                    Codes = session.Coupons,
                    Applied = [.. session.Discounts.Select(discount => new UcpAppliedDiscount
                    {
                        Code = session.Coupons.Contains(discount.Code, StringComparer.OrdinalIgnoreCase) ? discount.Code : null,
                        Title = discount.Label,
                        Amount = Minor(Math.Abs(discount.Amount), session.Currency),
                        Automatic = !session.Coupons.Contains(discount.Code, StringComparer.OrdinalIgnoreCase),
                    })],
                }
                : null,
            Order = session.OrderId is { } orderId
                ? new UcpOrderConfirmation { Id = orderId, PermalinkUrl = links.OrderPage(session.Culture, orderId) }
                : null,
        };
    }

    public static string Status(CheckoutState state) => state switch
    {
        CheckoutState.Incomplete => "incomplete",
        CheckoutState.ReadyForComplete => "ready_for_complete",
        CheckoutState.RequiresEscalation => "requires_escalation",
        CheckoutState.Completed => "completed",
        _ => "canceled",
    };

    /// <summary>
    /// Exactly one subtotal and one total, as the schema demands, and **no tax
    /// entry**: Tendero's prices include VAT (ADR 0028), so a tax entry would be
    /// counted twice by any platform that adds the entries up — which UCP tells
    /// platforms to do. The VAT is shown inside the total instead, as lines that
    /// sum to it, and only when they do.
    /// </summary>
    private static IReadOnlyList<UcpTotal> Totals(CheckoutSessionView session)
    {
        var totals = session.Totals;
        if (totals is null)
            return [new UcpTotal { Type = "subtotal", Amount = 0 }, new UcpTotal { Type = "total", Amount = 0 }];

        List<UcpTotal> entries = [new UcpTotal { Type = "subtotal", Amount = Minor(totals.Subtotal, session.Currency) }];
        if (totals.DiscountTotal != 0m)
            entries.Add(new UcpTotal { Type = "items_discount", Amount = -Minor(Math.Abs(totals.DiscountTotal), session.Currency) });
        if (session.SelectedShippingOption is not null)
            entries.Add(new UcpTotal { Type = "fulfillment", Amount = Minor(totals.Shipping, session.Currency) });

        List<UcpTotalLine> vat = [];
        foreach (var tax in totals.Taxes)
        {
            var rate = (tax.Rate * 100m).ToString("0.##", CultureInfo.InvariantCulture);
            vat.Add(new UcpTotalLine { DisplayText = session.Culture == "en" ? $"Base at {rate}% VAT" : $"Base al {rate} % de IVA", Amount = Minor(tax.Base, session.Currency) });
            vat.Add(new UcpTotalLine { DisplayText = session.Culture == "en" ? $"VAT {rate}%" : $"IVA {rate} %", Amount = Minor(tax.Amount, session.Currency) });
        }

        var total = Minor(totals.Total, session.Currency);
        entries.Add(new UcpTotal
        {
            Type = "total",
            Amount = total,
            DisplayText = session.Culture == "en" ? "Total (VAT included)" : "Total (IVA incluido)",
            Lines = vat.Count > 0 && vat.Sum(line => line.Amount) == total ? vat : null,
        });
        return entries;
    }

    private static UcpBuyer? Buyer(CheckoutBuyerDetails buyer) =>
        buyer == CheckoutBuyerDetails.Nobody
            ? null
            : new UcpBuyer { FirstName = buyer.FirstName, LastName = buyer.LastName, Email = buyer.Email, PhoneNumber = buyer.Phone };

    private static UcpFulfillment Fulfillment(CheckoutSessionView session, IReadOnlyList<string> lineIds) => new()
    {
        Methods =
        [
            new UcpFulfillmentMethod
            {
                Id = ShippingMethodId,
                Type = "shipping",
                LineItemIds = lineIds,
                Destinations = session.Destination is { } address ? [Destination(address)] : null,
                SelectedDestinationId = session.Destination is null ? null : DestinationId,
                Groups =
                [
                    new UcpFulfillmentGroup
                    {
                        Id = GroupId,
                        LineItemIds = lineIds,
                        Options = [.. session.ShippingOptions.Select(option => new UcpFulfillmentOption
                        {
                            Id = option.Code,
                            Title = option.Label,
                            Description = option.EstimatedDays is { } days
                                ? new UcpPlainDescription { Plain = session.Culture == "en" ? $"Arrives in {days} days" : $"Llega en {days} días" }
                                : null,
                            Totals = [new UcpTotal { Type = "total", Amount = Minor(option.Amount, session.Currency) }],
                        })],
                        SelectedOptionId = session.SelectedShippingOption,
                    },
                ],
            },
        ],
    };

    private static UcpDestination Destination(Address address)
    {
        var (first, last) = SplitName(address.RecipientName);
        return new UcpDestination
        {
            Id = DestinationId,
            Type = "shipping_address",
            FirstName = first,
            LastName = last,
            StreetAddress = address.Line1,
            ExtendedAddress = address.Line2,
            AddressLocality = address.City,
            AddressRegion = address.Region,
            PostalCode = address.PostalCode,
            AddressCountry = address.CountryCode,
            PhoneNumber = address.Phone,
        };
    }

    private static (string? First, string? Last) SplitName(string name)
    {
        var parts = name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch { 0 => (null, null), 1 => (parts[0], null), _ => (parts[0], parts[1]) };
    }

    // ---------- Messages ----------

    private static List<UcpCheckoutMessage> Messages(CheckoutSessionView session, IReadOnlyList<string> lineIds, bool fulfillment) =>
        [.. session.Issues.Select(issue => Message(issue, session, lineIds, fulfillment))];

    private static UcpCheckoutMessage Message(CheckoutIssue issue, CheckoutSessionView session, IReadOnlyList<string> lineIds, bool fulfillment)
    {
        var en = session.Culture == "en";
        var path = Path(issue.Target, session, lineIds, fulfillment);
        var (type, severity) = issue.Code switch
        {
            CheckoutIssueCodes.DiscountCodeInvalid or CheckoutIssueCodes.QuantityAdjusted => ("warning", (string?)null),
            // The one a person has to answer: requires_escalation says so (UCP §Status Values).
            CheckoutIssueCodes.PaymentNotSettled => ("error", "requires_buyer_input"),
            // AP2's (ADR 0040): what the platform must do next — send one, ask the
            // person again, or give up on a mandate that will never verify.
            Ap2.Ap2ErrorCodes.MandateRequired or Ap2.Ap2ErrorCodes.MandateExpired => ("error", "recoverable"),
            Ap2.Ap2ErrorCodes.MandateScopeMismatch or Ap2.Ap2ErrorCodes.MerchantAuthorizationInvalid => ("error", "requires_buyer_review"),
            Ap2.Ap2ErrorCodes.AgentMissingKey or Ap2.Ap2ErrorCodes.MandateInvalidSignature => ("error", "unrecoverable"),
            // The shop's own rules (P11-6): a person decides — the shopkeeper, or
            // the buyer finishing on the storefront.
            CheckoutIssueCodes.AgentRefused => ("error", "requires_buyer_input"),
            _ => ("error", "recoverable"),
        };

        return new UcpCheckoutMessage
        {
            Type = type,
            Code = issue.Code,
            Path = path,
            Severity = severity,
            Content = Content(issue, en),
        };
    }

    private static string Path(string target, CheckoutSessionView session, IReadOnlyList<string> lineIds, bool fulfillment)
    {
        if (target.StartsWith("line:", StringComparison.Ordinal))
        {
            var index = lineIds.ToList().FindIndex(id => string.Equals(id, target[5..], StringComparison.OrdinalIgnoreCase));
            return index < 0 ? "$.line_items" : $"$.line_items[{index}]";
        }

        if (target.StartsWith("coupon:", StringComparison.Ordinal))
        {
            var index = session.Coupons.ToList().FindIndex(code => string.Equals(code, target[7..], StringComparison.OrdinalIgnoreCase));
            return index < 0 ? "$.discounts.codes" : $"$.discounts.codes[{index}]";
        }

        return target switch
        {
            "buyer.email" => "$.buyer.email",
            "fulfillment.destination" => fulfillment ? "$.fulfillment.methods[0].destinations" : "$",
            "fulfillment.option" => fulfillment ? "$.fulfillment.methods[0].groups[0].selected_option_id" : "$",
            "payment" => "$.payment",
            "totals" => "$.totals",
            _ => "$.line_items",
        };
    }

    private static string Content(CheckoutIssue issue, bool en) => issue.Code switch
    {
        CheckoutIssueCodes.Missing => issue.Target switch
        {
            "buyer.email" => en ? "The buyer's email is needed to confirm the order." : "Hace falta el email del comprador para confirmar el pedido.",
            "fulfillment.destination" => en ? "A shipping address is needed." : "Hace falta una dirección de envío.",
            "fulfillment.option" => en ? "Choose a shipping option." : "Elige una opción de envío.",
            _ => en ? "The checkout has no items." : "El checkout no tiene artículos.",
        },
        CheckoutIssueCodes.ItemUnavailable => en ? "This item cannot be bought right now." : "Este artículo no se puede comprar ahora mismo.",
        CheckoutIssueCodes.QuantityAdjusted => en ? $"The quantity was lowered to the most a line takes ({issue.Detail})." : $"La cantidad se ha reducido al máximo que admite una línea ({issue.Detail}).",
        CheckoutIssueCodes.AddressUndeliverable => en ? "The shop does not deliver to this address." : "La tienda no entrega en esta dirección.",
        CheckoutIssueCodes.ShippingOptionUnavailable => en ? "That shipping option is not offered for this address." : "Esa opción de envío no se ofrece para esta dirección.",
        CheckoutIssueCodes.DiscountCodeInvalid => en ? "This code does not apply to this checkout." : "Este código no se aplica a este checkout.",
        CheckoutIssueCodes.PriceChanged => en ? "The price changed since it was shown; nothing was charged. Review the new totals and complete again." : "El precio ha cambiado desde que se mostró; no se ha cobrado nada. Revisa los nuevos totales y vuelve a completar.",
        CheckoutIssueCodes.PaymentFailed => en ? "The payment was declined." : "El pago ha sido rechazado.",
        CheckoutIssueCodes.PaymentUnavailable => en ? "The payment could not be processed right now; try again with the same key." : "No se ha podido procesar el pago ahora mismo; reinténtalo con la misma clave.",
        CheckoutIssueCodes.PaymentNotSettled => en ? "This shop does not take an agent's payment yet: a person completes the purchase at the continue URL." : "Esta tienda aún no cobra pagos de agentes: una persona completa la compra en la URL de continuación.",
        Ap2.Ap2ErrorCodes.MandateRequired => en ? "AP2 was negotiated: completing needs the buyer's checkout mandate." : "Se negoció AP2: para completar hace falta el mandato de compra del comprador.",
        Ap2.Ap2ErrorCodes.AgentMissingKey => en ? "The mandate names a key the platform's profile does not publish." : "El mandato nombra una clave que el perfil de la plataforma no publica.",
        Ap2.Ap2ErrorCodes.MandateInvalidSignature => en ? "The checkout mandate does not verify." : "El mandato de compra no se puede verificar.",
        Ap2.Ap2ErrorCodes.MandateExpired => en ? "The checkout mandate has expired: ask the buyer to approve again." : "El mandato de compra ha caducado: pide al comprador que lo apruebe de nuevo.",
        Ap2.Ap2ErrorCodes.MerchantAuthorizationInvalid => en ? "The checkout in the mandate is not one this shop signed." : "El checkout del mandato no lo ha firmado esta tienda.",
        CheckoutIssueCodes.AgentRefused => (issue.Detail?.Split(':')[0], en) switch
        {
            (Ordering.SuspendedAgentsPlaceNothing.RuleName, true) => "This shop has suspended this agent: the purchase can be finished by a person at the continue URL.",
            (Ordering.SuspendedAgentsPlaceNothing.RuleName, false) => "Esta tienda ha suspendido a este agente: una persona puede terminar la compra en la URL de continuación.",
            (Ordering.OrderCapByTrust.RuleName, true) => "The total is above what this shop lets this agent spend in one order: a person can finish it at the continue URL.",
            (Ordering.OrderCapByTrust.RuleName, false) => "El total supera lo que esta tienda deja gastar a este agente en un pedido: una persona puede terminarlo en la URL de continuación.",
            (Ordering.OrdersPerAgentPerHour.RuleName, true) => "This agent has placed as many orders this hour as the shop allows: try later, or a person can finish it at the continue URL.",
            (Ordering.OrdersPerAgentPerHour.RuleName, false) => "Este agente ya ha hecho los pedidos por hora que la tienda permite: prueba más tarde, o una persona puede terminarlo en la URL de continuación.",
            (_, true) => "This shop's rules for agents refused this purchase.",
            _ => "Las reglas de esta tienda para agentes han rechazado esta compra.",
        },
        Ap2.Ap2ErrorCodes.MandateScopeMismatch => en ? "The mandate approves other terms than this checkout's now: ask the buyer to approve these." : "El mandato aprueba otras condiciones que las de este checkout ahora: pide al comprador que apruebe estas.",
        _ => issue.Code,
    };

    // ---------- Requests ----------

    public static IReadOnlyList<CheckoutLineInput> Lines(UcpCheckoutRequest request) =>
        [.. (request.LineItems ?? []).Select(line => new CheckoutLineInput(line.Item?.Id ?? "", line.Quantity))];

    /// <summary>
    /// What the agent said beyond the lines. A destination that is not a whole
    /// address is no destination — the session says one is missing — rather than
    /// half an address frozen onto an order.
    /// </summary>
    public static CheckoutDetails Details(UcpCheckoutRequest request)
    {
        var buyer = request.Buyer is { } b
            ? new CheckoutBuyerDetails(b.FirstName, b.LastName, b.Email, b.PhoneNumber)
            : CheckoutBuyerDetails.Nobody;

        var method = request.Fulfillment?.Methods?.FirstOrDefault();
        var written = method?.Destinations?.FirstOrDefault();
        var destination = written is null ? null : Address(written, buyer);

        return new CheckoutDetails(
            buyer,
            destination,
            method?.Groups?.FirstOrDefault()?.SelectedOptionId,
            request.Discounts?.Codes ?? []);
    }

    private static Address? Address(UcpDestination destination, CheckoutBuyerDetails buyer)
    {
        var name = string.Join(' ', new[] { destination.FirstName, destination.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var buyerName = string.Join(' ', new[] { buyer.FirstName, buyer.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var recipient = name.Length > 0 ? name : buyerName;

        if (string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(destination.StreetAddress)
            || string.IsNullOrWhiteSpace(destination.AddressLocality) || string.IsNullOrWhiteSpace(destination.PostalCode)
            || destination.AddressCountry is not { Length: 2 })
            return null;

        return SharedKernel.Address.Create(
            recipient, destination.StreetAddress, destination.ExtendedAddress, destination.AddressLocality,
            destination.AddressRegion, destination.PostalCode, destination.AddressCountry.ToUpperInvariant(), destination.PhoneNumber);
    }

    /// <summary>
    /// Minor units: 39,00 € is 3900, and 3900 ¥ is 3900 too — the currency's own
    /// exponent, from the one table the shop's wire reads (<see cref="MinorUnits"/>).
    /// Away from zero, as a till would.
    /// </summary>
    public static long Minor(decimal amount, string currency) => MinorUnits.From(amount, currency);

    /// <summary>Minor units back to the amount: what a mandate approved, in the money Ordering speaks.</summary>
    public static decimal Major(long minor, string currency) => MinorUnits.ToAmount(minor, currency);
}
