using System.Text.Json;

namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// Every query the adapter sends, in one place, so the day Shopify's schema
/// moves there is one file to read against it. Checked against the
/// <see cref="ShopifyOptions.PinnedApiVersion"/> reference; the round trip
/// against a real development store (P16-6) is what proves them.
/// </summary>
internal static class ShopifyQueries
{
    /// <summary>
    /// The whole catalogue as one bulk operation. Four connections (products,
    /// their media, their metafields under one namespace, their variants) within
    /// the five and the two levels a bulk query allows. <c>groupObjects: true</c>
    /// because the file is read as a stream, each product's children right after
    /// it; since 2026-01 that is no longer the default. Each locale Tendero speaks besides the store's own is
    /// asked for by an alias named for its language, <c>t_es</c> for
    /// <c>es-ES</c> (an alias cannot carry the hyphen), on its product's line.
    /// </summary>
    public static string Products(IEnumerable<string> translatedLocales, string attributesNamespace = ShopifyOptions.DefaultAttributesNamespace)
    {
        var translations = string.Concat(translatedLocales.Select(locale =>
            $" t_{ShopifyLanguage.Of(locale)}: translations(locale: \\\"{locale}\\\") {{ key value }}"));

        return
            $$"""
            mutation {
              bulkOperationRunQuery(
                groupObjects: true
                query: "{ products { edges { node { id handle title descriptionHtml vendor publishedAt category { id fullName }{{translations}} media { edges { node { ... on MediaImage { id image { url altText } } } } } metafields(namespace: \"{{attributesNamespace}}\") { edges { node { id key value } } } variants { edges { node { id sku barcode price taxable selectedOptions { name value } image { url } } } } } } } }"
              ) {
                bulkOperation { id status }
                userErrors { field message }
              }
            }
            """;
    }

    /// <summary>
    /// Every variant's levels as one bulk operation (S2a, P16-10): two connections,
    /// the variants and their inventory item's levels, each level a line whose
    /// parent is its variant — the inventory item is an object, not a connection,
    /// so it has no line of its own. Only <c>on_hand</c>: a migration is a
    /// stocktake, and what is reserved or committed is the old shop's business.
    /// </summary>
    public const string InventoryLevels =
        """
        mutation {
          bulkOperationRunQuery(
            groupObjects: true
            query: "{ productVariants { edges { node { id inventoryItem { inventoryLevels { edges { node { location { id } quantities(names: [\"on_hand\"]) { name quantity } } } } } } } } }"
          ) {
            bulkOperation { id status }
            userErrors { field message }
          }
        }
        """;

    /// <summary>
    /// Every collection with its products' ids, as one bulk operation (P16-11):
    /// two connections, each product a line whose parent is its collection. The
    /// title is the store's own language, for a person reading a note.
    /// </summary>
    public const string Collections =
        """
        mutation {
          bulkOperationRunQuery(
            groupObjects: true
            query: "{ collections { edges { node { id handle title products { edges { node { id } } } } } } }"
          ) {
            bulkOperation { id status }
            userErrors { field message }
          }
        }
        """;

    /// <summary>
    /// Every customer with their addresses, as one bulk operation (S2b, P16-17):
    /// one connection inside another, each address a line whose parent is its
    /// customer. Through the fields current in the pinned version —
    /// <c>defaultEmailAddress</c> carries the email and its marketing consent,
    /// <c>addressesV2</c> the addresses — because <c>email</c>,
    /// <c>emailMarketingConsent</c> and <c>addresses</c> are deprecated in it.
    /// Customers are protected data: each field must have been granted to the app.
    /// </summary>
    public const string Customers =
        """
        mutation {
          bulkOperationRunQuery(
            groupObjects: true
            query: "{ customers { edges { node { id firstName lastName locale defaultEmailAddress { emailAddress marketingState marketingUpdatedAt } defaultAddress { id } addressesV2 { edges { node { id name address1 address2 city provinceCode zip countryCodeV2 phone } } } } } } }"
          ) {
            bulkOperation { id status }
            userErrors { field message }
          }
        }
        """;

    /// <summary>
    /// A page of orders, oldest first, with everything an order's history keeps
    /// (S2b, P16-18). Paged rather than bulk: a refund's lines are a connection
    /// inside the refunds list, and a bulk query refuses a connection under a
    /// list. Money is each figure's <c>shopMoney</c>, the store's currency, as
    /// the catalogue's prices are. A line's discounts are its allocations, named
    /// by the application's index, because an application carries a value (10 %)
    /// and not what it took off this order. 250 lines and 250 refunded lines per
    /// order are the most a page carries, far beyond a shop at this scale.
    /// </summary>
    public static string Orders(string? after)
    {
        var cursor = after is null ? "" : $", after: {JsonSerializer.Serialize(after)}";
        const string money = "{ shopMoney { amount currencyCode } }";
        const string address = "{ name address1 address2 city provinceCode zip countryCodeV2 phone }";
        const string allocations = $"discountAllocations {{ allocatedAmountSet {money} discountApplication {{ index }} }}";

        return
            $$"""
            { orders(first: 50, sortKey: CREATED_AT{{cursor}}) {
                pageInfo { hasNextPage endCursor }
                nodes {
                  id name createdAt cancelledAt displayFulfillmentStatus email customerLocale
                  customer { id }
                  totalDiscountsSet {{money}} totalTaxSet {{money}} totalPriceSet {{money}}
                  taxLines { title rate priceSet {{money}} }
                  shippingAddress {{address}} billingAddress {{address}}
                  shippingLine { title originalPriceSet {{money}} {{allocations}} }
                  fulfillments { createdAt }
                  discountApplications(first: 20) { nodes { index ... on DiscountCodeApplication { code } ... on AutomaticDiscountApplication { title } ... on ManualDiscountApplication { title } } }
                  lineItems(first: 250) { nodes { id title variantTitle quantity variant { id } originalUnitPriceSet {{money}} {{allocations}} } }
                  refunds { id createdAt totalRefundedSet {{money}} refundLineItems(first: 250) { nodes { quantity lineItem { id } } } }
                }
              } }
            """;
    }

    /// <summary>The store's locations; a store has a handful, so a page of 250 is all of them.</summary>
    public const string Locations = "{ locations(first: 250, includeInactive: true) { nodes { id name isActive } } }";

    public static string BulkOperation(string id) =>
        $$"""{ node(id: "{{id}}") { ... on BulkOperation { status errorCode objectCount url } } }""";

    public const string Shop = "{ shop { currencyCode } shopLocales(published: true) { locale primary } }";
}
