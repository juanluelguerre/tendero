namespace ElGuerre.Tendero.Catalog.Connectors.Shopify;

/// <summary>What went wrong talking to a store, as a closed set the import can name.</summary>
public enum ShopifyFailure
{
    /// <summary>The store refused the app's credentials, or a fresh token.</summary>
    Unauthorized,

    /// <summary>The store stayed throttled after every retry.</summary>
    Throttled,

    /// <summary>The store answered with GraphQL errors or user errors.</summary>
    Rejected,

    /// <summary>The bulk operation ended failed, cancelled or expired.</summary>
    BulkOperationFailed,

    /// <summary>
    /// A bulk file whose lines do not follow their parent: it was not grouped,
    /// and reading it as a stream would put a variant on the wrong product.
    /// </summary>
    UngroupedBulkFile,
}

public sealed class ShopifyException(ShopifyFailure failure, string message) : Exception(message)
{
    public ShopifyFailure Failure { get; } = failure;
}
