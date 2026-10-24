using System.Runtime.CompilerServices;
using System.Text.Json;
using ElGuerre.Tendero.Accounts.Ports;

namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// The customers port's Shopify adapter (phase 16, S2b, P16-17). It reads every
/// customer with their addresses in one bulk operation and hands them over in the
/// shop's words:
///
/// - the default email, the name and the language — the store's locale cut to a
///   language the shop speaks (<c>en-GB</c> is English; French is none);
/// - the addresses, the store's default marked;
/// - the email-marketing consent as the store dated it: <c>SUBSCRIBED</c> is a
///   consent given, <c>UNSUBSCRIBED</c> one withdrawn, and any other state — never
///   asked, pending a confirmation — records none.
///
/// No password comes; no platform exports one. Registered with the other Shopify
/// adapters when the store's credentials are configured.
/// </summary>
internal sealed class ShopifyCustomerConnector(ShopifyAdmin admin) : ICustomerSourceConnector
{
    public const string Key = "shopify";

    public string Source => Key;

    public async IAsyncEnumerable<SourceCustomer> StreamCustomersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var file = await admin.RunBulkAsync(ShopifyQueries.Customers, cancellationToken);
        if (file is null)
            yield break;

        CustomerReading? reading = null;
        await foreach (var line in admin.ReadLinesAsync(file, cancellationToken))
        {
            if (!line.TryGetProperty("__parentId", out var parent))
            {
                if (reading is not null)
                    yield return reading.Complete();

                reading = new CustomerReading(line);
                continue;
            }

            if (reading is null || ShopifyBulkProducts.IdOf(parent.GetString()) != reading.Id)
            {
                throw new ShopifyException(
                    ShopifyFailure.UngroupedBulkFile,
                    $"An address of {parent.GetString()} came while reading {reading?.Id ?? "nothing"}: the bulk file is not grouped.");
            }

            reading.Add(line);
        }

        if (reading is not null)
            yield return reading.Complete();
    }

    /// <summary>A customer while their addresses are read, as the bulk file groups them.</summary>
    private sealed class CustomerReading(JsonElement customer)
    {
        private readonly List<SourceAddress> addresses = [];
        private readonly string? defaultAddress = customer.TryGetProperty("defaultAddress", out var chosen)
            && chosen.ValueKind == JsonValueKind.Object ? Text(chosen, "id") : null;

        public string Id { get; } = ShopifyBulkProducts.IdOf(customer.GetProperty("id").GetString());

        public void Add(JsonElement address)
        {
            addresses.Add(new SourceAddress(
                Text(address, "name") ?? "",
                Text(address, "address1") ?? "",
                Text(address, "address2"),
                Text(address, "city") ?? "",
                Text(address, "provinceCode"),
                Text(address, "zip") ?? "",
                Text(address, "countryCodeV2") ?? "",
                Text(address, "phone"),
                IsDefault: Text(address, "id") == defaultAddress));
        }

        public SourceCustomer Complete()
        {
            var email = customer.TryGetProperty("defaultEmailAddress", out var address) && address.ValueKind == JsonValueKind.Object
                ? address
                : (JsonElement?)null;

            return new SourceCustomer(
                Id,
                email is { } known ? Text(known, "emailAddress") : null,
                Text(customer, "firstName"),
                Text(customer, "lastName"),
                ShopifyLanguage.SpokenOf(Text(customer, "locale")),
                email is { } consented ? ConsentOf(consented) : null,
                addresses);
        }

        /// <summary>Subscribed is given, unsubscribed withdrawn, each when the store says; any other state records none.</summary>
        private static SourceConsent? ConsentOf(JsonElement email)
        {
            var at = Text(email, "marketingUpdatedAt") is { } when ? DateTimeOffset.Parse(when, System.Globalization.CultureInfo.InvariantCulture) : (DateTimeOffset?)null;

            return (Text(email, "marketingState"), at) switch
            {
                ("SUBSCRIBED", { } given) => new SourceConsent(true, given),
                ("UNSUBSCRIBED", { } withdrawn) => new SourceConsent(false, withdrawn),
                _ => null,
            };
        }

        private static string? Text(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
