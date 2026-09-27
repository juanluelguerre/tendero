using System.Text.Json.Serialization;
using ElGuerre.Tendero.Ucp.Catalog;

namespace ElGuerre.Tendero.Ucp.Checkout;

// UCP's checkout (`shopping/checkout.json`, release 2026-08-25) and the two
// extensions Tendero declares with it — fulfillment and discount — as records
// that name every member the way the schema does. Money is in minor units.
// Nothing Tendero-shaped leaks through: the mapper translates, these carry.

// ---------- Response ----------

/// <summary>The checkout response's `ucp` member: the version, the capabilities active here, and the payment handlers offered to this caller.</summary>
public sealed record UcpCheckoutEnvelope
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "success";

    [JsonPropertyName("capabilities")]
    public required IReadOnlyDictionary<string, IReadOnlyList<UcpVersionOnly>> Capabilities { get; init; }

    /// <summary>Required, and may be empty: an empty map says the shop settles nothing for this caller.</summary>
    [JsonPropertyName("payment_handlers")]
    public required IReadOnlyDictionary<string, IReadOnlyList<UcpPaymentHandler>> PaymentHandlers { get; init; }
}

public sealed record UcpVersionOnly
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }
}

/// <summary>A payment handler instance: its id is what an instrument names as <c>handler_id</c>.</summary>
public sealed record UcpPaymentHandler
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("available_instruments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpAvailableInstrument>? AvailableInstruments { get; init; }

    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Config { get; init; }
}

public sealed record UcpAvailableInstrument
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}

public sealed record UcpCheckout
{
    [JsonPropertyName("ucp")]
    public required UcpCheckoutEnvelope Ucp { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("line_items")]
    public required IReadOnlyList<UcpLineItem> LineItems { get; init; }

    [JsonPropertyName("totals")]
    public required IReadOnlyList<UcpTotal> Totals { get; init; }

    [JsonPropertyName("links")]
    public required IReadOnlyList<UcpLink> Links { get; init; }

    [JsonPropertyName("buyer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UcpBuyer? Buyer { get; init; }

    [JsonPropertyName("messages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpCheckoutMessage>? Messages { get; init; }

    [JsonPropertyName("continue_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContinueUrl { get; init; }

    [JsonPropertyName("expires_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpiresAt { get; init; }

    [JsonPropertyName("fulfillment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UcpFulfillment? Fulfillment { get; init; }

    [JsonPropertyName("discounts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UcpDiscounts? Discounts { get; init; }

    [JsonPropertyName("order")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UcpOrderConfirmation? Order { get; init; }
}

public sealed record UcpLineItem
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("item")]
    public required UcpItem Item { get; init; }

    [JsonPropertyName("quantity")]
    public required int Quantity { get; init; }

    [JsonPropertyName("totals")]
    public required IReadOnlyList<UcpTotal> Totals { get; init; }
}

public sealed record UcpItem
{
    /// <summary>The variant's SKU, as the catalogue binding names it.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Unit price in minor units.</summary>
    [JsonPropertyName("price")]
    public required long Price { get; init; }

    [JsonPropertyName("image_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageUrl { get; init; }
}

/// <summary>One totals entry. Subtotal, fulfillment and total are positive; a discount is negative.</summary>
public sealed record UcpTotal
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("amount")]
    public required long Amount { get; init; }

    [JsonPropertyName("display_text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayText { get; init; }

    /// <summary>A breakdown whose amounts sum to <see cref="Amount"/> — Tendero's VAT, inside the total it already is part of.</summary>
    [JsonPropertyName("lines")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpTotalLine>? Lines { get; init; }
}

public sealed record UcpTotalLine
{
    [JsonPropertyName("display_text")]
    public required string DisplayText { get; init; }

    [JsonPropertyName("amount")]
    public required long Amount { get; init; }
}

public sealed record UcpLink
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }
}

public sealed record UcpBuyer
{
    [JsonPropertyName("first_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FirstName { get; init; }

    [JsonPropertyName("last_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastName { get; init; }

    [JsonPropertyName("email")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; init; }

    [JsonPropertyName("phone_number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PhoneNumber { get; init; }
}

/// <summary>An error, a warning or an info. An error carries a severity; the others do not.</summary>
public sealed record UcpCheckoutMessage
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }

    [JsonPropertyName("severity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Severity { get; init; }
}

public sealed record UcpPostalAddress
{
    [JsonPropertyName("first_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FirstName { get; init; }

    [JsonPropertyName("last_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastName { get; init; }

    [JsonPropertyName("street_address")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StreetAddress { get; init; }

    [JsonPropertyName("extended_address")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExtendedAddress { get; init; }

    [JsonPropertyName("address_locality")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AddressLocality { get; init; }

    [JsonPropertyName("address_region")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AddressRegion { get; init; }

    [JsonPropertyName("postal_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PostalCode { get; init; }

    [JsonPropertyName("address_country")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AddressCountry { get; init; }

    [JsonPropertyName("phone_number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PhoneNumber { get; init; }
}

/// <summary>A shipping destination: a postal address with the id and type a business response always carries.</summary>
public sealed record UcpDestination
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("first_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FirstName { get; init; }

    [JsonPropertyName("last_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastName { get; init; }

    [JsonPropertyName("street_address")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StreetAddress { get; init; }

    [JsonPropertyName("extended_address")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExtendedAddress { get; init; }

    [JsonPropertyName("address_locality")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AddressLocality { get; init; }

    [JsonPropertyName("address_region")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AddressRegion { get; init; }

    [JsonPropertyName("postal_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PostalCode { get; init; }

    [JsonPropertyName("address_country")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AddressCountry { get; init; }

    [JsonPropertyName("phone_number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PhoneNumber { get; init; }
}

public sealed record UcpFulfillment
{
    [JsonPropertyName("methods")]
    public IReadOnlyList<UcpFulfillmentMethod>? Methods { get; init; }
}

public sealed record UcpFulfillmentMethod
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; init; }

    [JsonPropertyName("line_item_ids")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? LineItemIds { get; init; }

    [JsonPropertyName("destinations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpDestination>? Destinations { get; init; }

    [JsonPropertyName("selected_destination_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SelectedDestinationId { get; init; }

    [JsonPropertyName("groups")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpFulfillmentGroup>? Groups { get; init; }
}

public sealed record UcpFulfillmentGroup
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonPropertyName("line_item_ids")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? LineItemIds { get; init; }

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpFulfillmentOption>? Options { get; init; }

    [JsonPropertyName("selected_option_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SelectedOptionId { get; init; }
}

public sealed record UcpFulfillmentOption
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>An object, not a string — two of the specification's own examples get this wrong.</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UcpPlainDescription? Description { get; init; }

    [JsonPropertyName("totals")]
    public required IReadOnlyList<UcpTotal> Totals { get; init; }
}

public sealed record UcpPlainDescription
{
    [JsonPropertyName("plain")]
    public required string Plain { get; init; }
}

public sealed record UcpDiscounts
{
    [JsonPropertyName("codes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Codes { get; init; }

    [JsonPropertyName("applied")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpAppliedDiscount>? Applied { get; init; }
}

public sealed record UcpAppliedDiscount
{
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Positive, in minor units: the schema's applied amount is an `amount`, not a signed one.</summary>
    [JsonPropertyName("amount")]
    public required long Amount { get; init; }

    [JsonPropertyName("automatic")]
    public bool Automatic { get; init; }
}

public sealed record UcpOrderConfirmation
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("permalink_url")]
    public required string PermalinkUrl { get; init; }
}

/// <summary>What UCP answers when there is no checkout to show: `common/types/error_response.json`.</summary>
public sealed record UcpErrorResponse
{
    [JsonPropertyName("ucp")]
    public required UcpEnvelopeWithStatus Ucp { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<UcpCheckoutMessage> Messages { get; init; }

    [JsonPropertyName("continue_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContinueUrl { get; init; }
}

public sealed record UcpEnvelopeWithStatus
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "error";
}

// ---------- Requests ----------

/// <summary>A create or an update — the update is a full replacement, so they share a shape.</summary>
public sealed record UcpCheckoutRequest
{
    [JsonPropertyName("line_items")]
    public IReadOnlyList<UcpLineItemRequest>? LineItems { get; init; }

    [JsonPropertyName("buyer")]
    public UcpBuyer? Buyer { get; init; }

    [JsonPropertyName("fulfillment")]
    public UcpFulfillment? Fulfillment { get; init; }

    [JsonPropertyName("discounts")]
    public UcpDiscounts? Discounts { get; init; }

    [JsonPropertyName("context")]
    public UcpCheckoutContext? Context { get; init; }
}

public sealed record UcpLineItemRequest
{
    [JsonPropertyName("item")]
    public UcpItemReference? Item { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }
}

public sealed record UcpItemReference
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

/// <summary>The buyer's context; only the language is read — an agent has no browser locale (ADR 0013).</summary>
public sealed record UcpCheckoutContext
{
    [JsonPropertyName("language")]
    public string? Language { get; init; }
}

public sealed record UcpCompleteRequest
{
    [JsonPropertyName("payment")]
    public UcpPaymentRequest? Payment { get; init; }

    /// <summary>Required at complete once AP2's mandate extension is negotiated, and read only then.</summary>
    [JsonPropertyName("ap2")]
    public UcpAp2CompleteRequest? Ap2 { get; init; }
}

/// <summary>AP2's member of a complete request: the checkout mandate, an SD-JWT+kb.</summary>
public sealed record UcpAp2CompleteRequest
{
    [JsonPropertyName("checkout_mandate")]
    public string? CheckoutMandate { get; init; }
}

public sealed record UcpPaymentRequest
{
    [JsonPropertyName("instruments")]
    public IReadOnlyList<UcpInstrumentRequest>? Instruments { get; init; }
}

public sealed record UcpInstrumentRequest
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("handler_id")]
    public string? HandlerId { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("credential")]
    public UcpCredentialRequest? Credential { get; init; }
}

public sealed record UcpCredentialRequest
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("token")]
    public string? Token { get; init; }
}
